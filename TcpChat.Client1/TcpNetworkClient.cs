using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Documents;
using TcpChat.Client.Core;

namespace TcpChat.Client
{
    public class TcpNetworkClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _userName;

        private TcpClient? _client;
        private NetworkStream? _stream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private bool _isConnected;

        // --- КРИПТОГРАФИЯ (E2EE ТОЛЬКО ДЛЯ ПРИВАТНЫХ ЧАТОВ) ---

        /// <summary>
        /// Криптографический «движок». Работает на основе математики эллиптических кривых (алгоритм Диффи-Хеллмана).
        /// </summary>
        private ECDiffieHellman _ecdh;
        private byte[] _myPublicKeyBytes = null!;// Публичный ключ в виде массива байт
        private readonly Dictionary<int, byte[]> _sessionKeys = new();// Хранилище общих AES-ключей: Key = ID собеседника, Value = Секретный ключ (32 байта)
        private List<User> _cachedUsers = new();// Локальный кэш пользователей для поиска их публичных ключей при получении сообщений

        public TcpNetworkClient(string host, int port, string userName)
        {
            _host = host;
            _port = port;
            _userName = userName ?? "Аноним";
            _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);// Инициализация криптографии на эллиптических кривых(NIST P-256)
        }

        // СОБЫТИЯ ДЛЯ WPF
        public event Action<List<User>>? OnUsersListReceived;// Когда пришел список пользователей
        public event Action<string, string>? OnBroadcastMessageReceived;// Без шифрования
        public event Action<int, string, string>? OnPrivateMessageReceived;// Сквозное шифрование (E2EE)
        public event Action<string>? OnConnectionStatusChanged;// Для логов/статуса подключения

        public async Task ConnectionAsync()
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_host, _port);
                _isConnected = true;
                OnConnectionStatusChanged?.Invoke($"Подключение к {_host} установлено");

                _stream = _client.GetStream();
                _reader = new StreamReader(_stream, Encoding.UTF8);
                _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

                _myPublicKeyBytes = _ecdh.ExportSubjectPublicKeyInfo();// Экспорт открытого ключа в массив байт

                NetworkPacket networkPacket = new NetworkPacket
                {
                    Command = "CONNECT",
                    SenderName = _userName,
                    MessageText = Convert.ToBase64String(_myPublicKeyBytes)// Конвертация публичного ключа в формат Base64, чтобы сервер сохранил его в User.PublicKeyBase64
                };

                await _writer.WriteLineAsync(JsonSerializer.Serialize(networkPacket));

                _ = Task.Run(() => ReceiveMessageAsync(_reader));
            }
            catch (SocketException ex)
            {
                Disconnect();
                OnConnectionStatusChanged?.Invoke($"Не удалось установить соединение. Ошибка: {ex.Message}");
            }
            catch (Exception ex)
            {
                Disconnect();
                OnConnectionStatusChanged?.Invoke($"Произошла ошибка в работе клиента: {ex.Message}");
            }
        }

        private async Task ReceiveMessageAsync(StreamReader reader)
        {
            try
            {
                string? responseLine;
                while (_isConnected && (responseLine = await reader.ReadLineAsync()) != null)
                {
                    NetworkPacket? networkPacket = JsonSerializer.Deserialize<NetworkPacket>(responseLine);
                    if (networkPacket == null) continue;

                    switch (networkPacket.Command)
                    {
                        case "USERS_LIST":
                            var users = JsonSerializer.Deserialize<List<User>>(networkPacket.MessageText);
                            if (users != null)
                            {
                                _cachedUsers = users;// Обновление локального кэша (нужен для извлечения публичных ключей собеседников)
                                OnUsersListReceived?.Invoke(users);
                            }
                            break;

                        case "RECEIVE_ALL":
                            OnBroadcastMessageReceived?.Invoke(networkPacket.SenderName, networkPacket.MessageText);
                            break;

                        case "RECEIVE_PRIVATE":
                            int senderId = networkPacket.TargetUserId;// Сервер при пересылке записывает в TargetUserId (или новое поле) ID отправителя.
                            var senderUser = _cachedUsers.FirstOrDefault(u => u.Id == senderId);// Поиск отправителя в кэше, чтобы взять его PublicKeyBase64
                            if (senderUser != null && !string.IsNullOrEmpty(senderUser.PublicKeyBase64))
                            {
                                try
                                {
                                    byte[] sessionKey = GetOrCreateSessionKey(senderUser.Id, senderUser.PublicKeyBase64);// Генерация общего AES-ключа для этого собеседника
                                    string decryptedText = DecryptText(networkPacket.MessageText, sessionKey);// Дешифрофка текста
                                    OnPrivateMessageReceived?.Invoke(senderId, networkPacket.SenderName, decryptedText);// Передача в UI уже безопасный и чистый текст
                                }
                                catch (CryptographicException)
                                {
                                    OnPrivateMessageReceived?.Invoke(senderId, networkPacket.SenderName, "[Ошибка: Не удалось расшифровать сообщение]");
                                }
                            }
                            else
                            {
                                OnPrivateMessageReceived?.Invoke(senderId, networkPacket.SenderName, "[Ошибка: Нет ключа для расшифровки]");
                            }
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is IOException)
            {
                OnConnectionStatusChanged?.Invoke("Соединение с сервером закрыто.");
            }
            catch (Exception ex)
            {
                OnConnectionStatusChanged?.Invoke($"[ОШИБКА ПРИЕМА]: {ex.Message}");
            }
            finally
            {
                Disconnect();
            }
        }

        // ОБЩИЙ ЧАТ (без шифрования)
        public async Task SendToAllAsync(string messageText)
        {
            if (!_isConnected || _writer == null || string.IsNullOrWhiteSpace(messageText)) return;

            NetworkPacket networkPacket = new NetworkPacket
            {
                Command = "MESSAGE_TO_ALL",
                MessageText = messageText
            };

            await _writer.WriteLineAsync(JsonSerializer.Serialize(networkPacket));
        }

        // ПРИВАТНЫЙ ЧАТ: Сквозное шифрование (E2EE)
        public async Task SendPrivateAsync(User targetUser, string messageText)
        {
            if (!_isConnected || _writer == null || string.IsNullOrWhiteSpace(messageText)) return;
            if (string.IsNullOrEmpty(targetUser.PublicKeyBase64)) throw new InvalidOperationException("У пользователя нет публичного ключа для шифрования.");
            byte[] sessionKey = GetOrCreateSessionKey(targetUser.Id, targetUser.PublicKeyBase64);// Генерация общего секретного AES-ключа с этим пользователем
            string encryptedText = EncryptText(messageText, sessionKey);// Шифрование текста сообщения алгоритмом AES-GCM
            NetworkPacket networkPacket = new NetworkPacket
            {
                Command = "MESSAGE_PRIVATE",
                TargetUserId = targetUser.Id,
                MessageText = encryptedText
            };

            await _writer.WriteLineAsync(JsonSerializer.Serialize(networkPacket));
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ КРИПТОГРАФИЧЕСКИЕ МЕТОДЫ ---

        private byte[] GetOrCreateSessionKey(int targetUserId, string targetUserPublicKeyBase64)
        {
            if (_sessionKeys.TryGetValue(targetUserId, out var existingKey)) return existingKey;// Проверка на наличие AES-ключа в локальном словаре
            byte[] otherPublicKeyBytes = Convert.FromBase64String(targetUserPublicKeyBase64);// Декодировка текстовой Base64-строки публичного ключа собеседника обратно в массив байт
            using ECDiffieHellman otherEcdh = ECDiffieHellman.Create();// Создание временного криптографического объекта, в который загрузится байты ключа собеседника
            otherEcdh.ImportSubjectPublicKeyInfo(otherPublicKeyBytes, out _);// Импортирование байтов публичного ключа в созданный объект. Символ `out _` означает игнорирование количества прочитанных байт.
            byte[] sharedSecret = _ecdh.DeriveKeyMaterial(otherEcdh.PublicKey);// Вычисление общего секрета (объект `_ecdh` берет свой секретный приватный ключ (из памяти ПК) и математически смешивает его с публичным ключом собеседника)

            // ХЕШИРОВАНИЕ: Массив `sharedSecret` может иметь произвольную длину. 
            // Пропуск его через SHA-256, чтобы получить фиксированный и хаотичный массив размером ровно 32 байта (256 бит).
            // Это идеальный размер для надежного симметричного ключа шифрования AES-256.
            byte[] aesKey = SHA256.HashData(sharedSecret);

            _sessionKeys[targetUserId] = aesKey;// Сохранение свежесозданного ключа в словарь `_sessionKeys` под ID этого пользователя, чтобы при отправке следующего сообщения не тратить ресурсы процессора на повторные вычисления.

            return aesKey;// Возвращение готового ключа для использования в методах шифрования (EncryptText) или дешифрования (DecryptText).
        }

        private string EncryptText(string plainText, byte[] key)
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);// конвертация текста сообщения (строку C#) в массив байт в кодировке UTF-8

            // Вычисление размеров блоков
            byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];// Создание пустого массива для Nonce (одноразового числа). Для алгоритма AES-GCM его размер строго фиксирован — 12 байт
            byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];// Создание пустого массива для аутентификационного тега (подписи). Для AES-GCM его стандартный размер — 16 байт
            byte[] cipherBytes = new byte[plainBytes.Length];// Создание пустого массив для будущего зашифрованного текста. Его размер будет в точности равен размеру исходного сообщения.

            // Заполнение массива nonce криптографически стойкими случайными байтами. 
            // Это гарантирует, что если дважды отправлено слово "Привет", зашифрованный текст каждый раз будет абсолютно уникальным.
            RandomNumberGenerator.Fill(nonce);

            // Шифрование через AesGcm
            using AesGcm aesGcm = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);// Инициализация криптографического движка AES-GCM. В него передается секретный AES-ключ (32 байта) и размер тега
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);// Алгоритм берет plainBytes, шифрует их на основе ключа и nonce, записывает результат в cipherBytes, а в массив tag записывает уникальную цифровую подпись сообщения
            byte[] result = new byte[nonce.Length + tag.Length + cipherBytes.Length];// Создание одного общего массива, размер которого равен сумме длин всех трех криптографических компонентов

            // Склеивание массива
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);// Копирование 12 байт случайного nonce в самое начало нашего общего массива (позиция 0)
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);// Копирование 16 байт защитного тега сразу вслед за nonce (смещение вправо на длину nonce)
            Buffer.BlockCopy(cipherBytes, 0, result, nonce.Length + tag.Length, cipherBytes.Length);// Копирование зашифрованного текста сообщения в самый конец массива (смещение вправо на длину nonce + tag)

            return Convert.ToBase64String(result);// Конвертация итогового склеенного массив байт в текстовый формат Base64. Теперь эту безопасную строку (без спецсимволов) можно легко передать внутри обычного JSON-пакета на сервер.

        }
        private string DecryptText(string encryptedBase64, byte[] key)
        {
            byte[] encryptedData = Convert.FromBase64String(encryptedBase64);// Переводит входящую Base64-строку в массив байт

            // Вычисление размеров блоков
            int nonceSize = AesGcm.NonceByteSizes.MaxSize;// Размер nonce (одноразового числа) в .NET фиксирован для AesGcm и равен 12 байтам
            int tagSize = AesGcm.TagByteSizes.MaxSize;// Размер tag (аутентификационного тега) равен 16 байтам
            int cipherSize = encryptedData.Length - nonceSize - tagSize;// Оставшаяся часть массива — это и есть чистый зашифрованный текст

            byte[] nonce = new byte[nonceSize];// Создается пустой массив байт той же длины, что и одноразовое число
            byte[] tag = new byte[tagSize];// Создается пустой массив байт той же длины, что и аутентификационный тег
            byte[] cipherBytes = new byte[cipherSize];// Создается пустой массив байт той же длины, что и зашифрованный текст

            // Разрезаем массив обратно (высокоскоростной метод копирования байт)
            Buffer.BlockCopy(encryptedData, 0, nonce, 0, nonceSize);
            Buffer.BlockCopy(encryptedData, nonceSize, tag, 0, tagSize);
            Buffer.BlockCopy(encryptedData, nonceSize + tagSize, cipherBytes, 0, cipherSize);

            // Дешифрование через AesGcm
            byte[] decryptedBytes = new byte[cipherSize];// Создается пустой массив байт той же длины, что и зашифрованный текст
            using AesGcm aesGcm = new AesGcm(key, tagSize);// Инициализация криптографического движка AES-GCM. В него передается секретный AES-ключ (32 байта) и размер тега
            aesGcm.Decrypt(nonce, cipherBytes, tag, decryptedBytes);// Дешифровка + Проверка целостности (на основе tag)
            return Encoding.UTF8.GetString(decryptedBytes);// Конвертация расшифрованного массива байт обратно в привычную C#-строку
        }
        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;
            _writer?.Dispose();
            _reader?.Dispose();
            _stream?.Dispose();
            _client?.Dispose();
            _ecdh?.Dispose();
            _sessionKeys.Clear();
            _cachedUsers.Clear();
            OnConnectionStatusChanged?.Invoke("Клиент отключен.");
        }
        public void Dispose()
        {
            Disconnect();
        }
    }
}
