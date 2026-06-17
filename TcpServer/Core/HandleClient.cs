using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TcpServer.Core
{
    public class HandleClient
    {
        private readonly DbOperations _dbOperations;
        private readonly ConcurrentDictionary<int, ConnectedClient> _connectedClients;
        private readonly TcpClient _tcpClient;

        public HandleClient(TcpClient tcpClient, DbOperations dbOperations, ConcurrentDictionary<int, ConnectedClient> connectedClients)
        {
            _tcpClient = tcpClient;
            _dbOperations = dbOperations;
            _connectedClients = connectedClients;
        }
        public async Task HandleClientAsync()
        {
            using (_tcpClient)
            {
                using NetworkStream stream = _tcpClient.GetStream();
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                using StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                string clientEndPoint = _tcpClient.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
                string clientName = clientEndPoint;
                string? rawMessage;
                int? currentUserId = null;
                bool isFirstMessage = true;

                try
                {
                    while ((rawMessage = await reader.ReadLineAsync()) != null)
                    {
                        rawMessage = rawMessage.Trim();
                        if (string.IsNullOrEmpty(rawMessage)) continue;
                        NetworkPacket? networkPacket = JsonSerializer.Deserialize<NetworkPacket>(rawMessage);
                        if (networkPacket == null) continue;
                        if (isFirstMessage && networkPacket.Command != "CONNECT")
                        {
                            Console.WriteLine($"[СЕРВЕР] {clientEndPoint} нарушил протокол. Отключение.");
                            break;
                        }
                        if (!isFirstMessage && !currentUserId.HasValue) break;
                        switch (networkPacket.Command)
                        {
                            case "CONNECT":
                                if (!isFirstMessage) continue;

                                isFirstMessage = false;
                                clientName = networkPacket.SenderName;
                                currentUserId = await _dbOperations.DbRegistrationAsync(clientName);

                                ConnectedClient connectedClient = new ConnectedClient(currentUserId.Value, clientName, writer)
                                {
                                    PublicKeyBase64 = networkPacket.MessageText
                                };

                                _connectedClients.AddOrUpdate(currentUserId.Value, connectedClient, (key, oldClient) =>
                                {
                                    try { oldClient.Writer.Dispose(); } catch { }
                                    return connectedClient;
                                });

                                Console.WriteLine($"[СЕРВЕР] Пользователь {clientName} (ID: {currentUserId}) подключился с ключом: {connectedClient.PublicKeyBase64?.Substring(0, 10)}...");

                                await BroadcastMessageAsync("СЕРВЕР", $"{clientName} вошел в чат.", currentUserId.Value);
                                await BroadcastUsersListAsync();

                                break;

                            case "MESSAGE_TO_ALL":
                                await _dbOperations.DbSaveBroadcastMessageAsync(currentUserId!.Value, networkPacket.MessageText);
                                await BroadcastMessageAsync(clientName, networkPacket.MessageText, currentUserId.Value);
                                break;

                            case "MESSAGE_PRIVATE":
                                await _dbOperations.DbSavePrivateMessageAsync(currentUserId!.Value, networkPacket.TargetUserId, networkPacket.MessageText);

                                if (_connectedClients.TryGetValue(networkPacket.TargetUserId, out var recipientClient))
                                {
                                    NetworkPacket privatePacket = new NetworkPacket
                                    {
                                        Command = "RECEIVE_PRIVATE",
                                        TargetUserId = currentUserId.Value,
                                        SenderName = clientName,
                                        MessageText = networkPacket.MessageText
                                    };
                                    await recipientClient.Writer.WriteLineAsync(JsonSerializer.Serialize(privatePacket));
                                }
                                break;

                            default:
                                Console.WriteLine($"[СЕРВЕР] Получена неизвестная команда: {networkPacket.Command} от {clientName}");
                                break;
                        }

                        Console.WriteLine($"[СЕРВЕР] Обработана команда {networkPacket.Command} от {clientName}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[СЕРВЕР] Ошибка связи с {clientName}: {ex.Message}");
                }
                finally
                {
                    if (currentUserId.HasValue)
                    {
                        _connectedClients.TryRemove(currentUserId.Value, out _);
                        Console.WriteLine($"[СЕРВЕР] {clientName} покинул чат.");

                        await BroadcastMessageAsync("СЕРВЕР", $"{clientName} покинул чат.", currentUserId.Value);
                        await BroadcastUsersListAsync();
                    }
                }
                Console.WriteLine("[СЕРВЕР] Соединение с клиентом успешно закрыто.\n");
            }
        }
        private async Task BroadcastMessageAsync(string senderName, string message, int excludeUserId)
        {
            Console.WriteLine($"[СЕРВЕР] Отправка сообщения \"{message}\" от {senderName} в общий чат.");
            NetworkPacket networkPacket = new NetworkPacket
            {
                Command = "RECEIVE_ALL",
                SenderName = senderName,
                MessageText = message
            };
            string jsonResponse = JsonSerializer.Serialize(networkPacket);
            foreach (var client in _connectedClients)
            {
                if (client.Key == excludeUserId) continue;
                try { await client.Value.Writer.WriteLineAsync(jsonResponse); }
                catch { Console.WriteLine($"[СЕРВЕР] Не удалось доставить сообщение для ID {client.Key}"); }
            }
        }
        private async Task BroadcastUsersListAsync()
        {
            try
            {
                // 1. Получаем список всех зарегистрированных пользователей из БД
                var allUsers = await _dbOperations.GetAllUsersAsync();

                // 2. Обогащаем список пользователей их публичными ключами из оперативной памяти сервера
                foreach (var user in allUsers)
                {
                    if (_connectedClients.TryGetValue(user.Id, out var connected))
                    {
                        user.PublicKeyBase64 = connected.PublicKeyBase64;
                    }
                }

                // 3. Сериализуем и отправляем список клиентам
                NetworkPacket usersPacket = new NetworkPacket
                {
                    Command = "USERS_LIST",
                    MessageText = JsonSerializer.Serialize(allUsers)
                };

                string jsonResponse = JsonSerializer.Serialize(usersPacket);
                foreach (var client in _connectedClients)
                {
                    try
                    {
                        Console.WriteLine($"[СЕРВЕР] Отправка списка пользователей {client.Key}.");
                        await client.Value.Writer.WriteLineAsync(jsonResponse);
                    }
                    catch { Console.WriteLine($"[СЕРВЕР] Не удалось отправить список пользователей для ID {client.Key}"); }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[СЕРВЕР ОШИБКА РАССЫЛКИ СПИСКА]: {ex.Message}");
            }
        }
    }
}
