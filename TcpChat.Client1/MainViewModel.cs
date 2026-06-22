using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using TcpChat.Client.Core;

namespace TcpChat.Client
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // Закрытое поле для хранения экземпляра сетевого клиента TCP (может быть null, если не создан)
        private TcpNetworkClient? _client;

        // Закрытое поле для хранения текущего выбранного пользователя в списке интерфейса (null, если никто не выбран)
        private User? _selectedUser;

        // Закрытое поле для хранения текущего текста сообщения из поля ввода (по умолчанию пустая строка)
        private string _messageText = string.Empty;

        // Закрытое поле для имени пользователя, генерирует случайное имя по умолчанию (например, User_452)
        private string _userName = "User_" + new Random().Next(100, 999);

        // Закрытое поле для хранения текста текущего сетевого статуса приложения
        private string _statusText = "Отключен";

        // Закрытое поле-флаг, сигнализирующее об отсутствии подключения (используется для блокировки/активации элементов интерфейса)
        private bool _isNotConnected = true;

        // Хранилище истории сообщений в памяти: Ключ = ID Пользователя (0 для общего чата), Значение = Список строк-сообщений
        private readonly Dictionary<int, List<string>> _chatHistories = new();

        // Публичное свойство-коллекция для списка пользователей, автоматически обновляющая связанный UI при добавлении/удалении
        public ObservableCollection<User> Users { get; set; } = new();

        // Публичное свойство-коллекция для отображения строк сообщений текущего активного чата на экране
        public ObservableCollection<string> ChatMessages { get; set; } = new();

        // Статичный виртуальный объект пользователя, выступающий в качестве заглушки для переключения на общую комнату чата
        private readonly User _allUsersItem = new User { Id = 0, Name = "--- ОБЩИЙ ЧАТ ---" };

        // Публичное свойство для привязки имени пользователя к UI
        public string UserName
        {
            // Возвращает текущее значение приватного поля
            get => _userName;
            // Устанавливает новое имя и мгновенно уведомляет интерфейс об изменении через OnPropertyChanged
            set { _userName = value; OnPropertyChanged(); }
        }

        // Публичное свойство для вывода текущего сетевого статуса
        public string StatusText
        {
            // Возвращает текущую строку статуса
            get => _statusText;
            // Обновляет строку статуса и посылает сигнал UI для перерисовки текста
            set { _statusText = value; OnPropertyChanged(); }
        }

        // Публичное свойство для управления доступностью элементов интерфейса (доступность кнопки "Подключиться")
        public bool IsNotConnected
        {
            // Возвращает true, если клиент еще не подключен к серверу
            get => _isNotConnected;
            // Изменяет состояние флага и уведомляет UI, меняя доступность (IsEnabled) элементов формы
            set { _isNotConnected = value; OnPropertyChanged(); }
        }

        // Публичное свойство для привязки к выбранному элементу (SelectedItem) в списке пользователей ListBox/ListView
        public User? SelectedUser
        {
            // Возвращает выбранного в данный момент пользователя
            get => _selectedUser;
            // Логика срабатывает при клике пользователя по новому собеседнику в списке
            set
            {
                // Перезаписывает приватное поле ссылкой на выбранного пользователя
                _selectedUser = value;
                // Уведомляет интерфейс, чтобы подсветить выбранную строку в списке
                OnPropertyChanged();

                // Вызывает ваш внутренний метод, который очищает экран и загружает историю сообщений для выбранного ID
                LoadChatHistory();
            }
        }

        // Публичное свойство для привязки к текстовому полю ввода сообщения (TextBox)
        public string MessageText
        {
            // Возвращает текст, который пользователь успел набрать в поле ввода
            get => _messageText;
            // Обновляет текст и дает знать командам и UI, что текст изменился
            set { _messageText = value; OnPropertyChanged(); }
        }

        // Команда интерфейса, к которой привязывается кнопка "Подключиться"
        public ICommand ConnectCommand { get; }

        // Команда интерфейса, к которой привязывается кнопка "Отправить"
        public ICommand SendCommand { get; }

        // Конструктор класса MainViewModel, выполняющий начальную инициализацию бизнес-логики
        public MainViewModel()
        {
            // Регистрирует команду подключения, которая при вызове запускает метод Connect
            ConnectCommand = new RelayCommand(_ => Connect());

            // Регистрирует команду отправки: запускает SendMessage, но активна (нажимаема) только если текст сообщения не пустой
            SendCommand = new RelayCommand(_ => SendMessage(), _ => !string.IsNullOrWhiteSpace(MessageText));

            // Сразу при старте добавляет пункт "--- ОБЩИЙ ЧАТ ---" в коллекцию пользователей чата
            Users.Add(_allUsersItem);

            // Принудительно устанавливает фокус и выбор на общий чат по умолчанию
            SelectedUser = _allUsersItem;
        }

        // ПОДКЛЮЧЕНИЕ
        // Синхронный метод-обертка для запуска асинхронного процесса подключения "в пожарном режиме" (без ожидания результата)
        private void Connect() => _ = ConnectAsync();
        // Асинхронный метод подключения
        private async Task ConnectAsync()
        {
            // Если поле имени пользователя пустое или состоит из пробелов — прерываем выполнение метода
            if (string.IsNullOrWhiteSpace(UserName)) return;

            // Меняем статус на экране, информируя пользователя о начале процесса
            StatusText = "Подключение...";

            // Создаем новый экземпляр сетевого клиента, передавая IP-адрес, порт и имя пользователя
            _client = new TcpNetworkClient("127.0.0.1", 8888, UserName);

            // Подписываемся на событие изменения сетевого статуса от клиента
            _client.OnConnectionStatusChanged += status =>
                // При возникновении события перенаправляем обновление свойства StatusText в главный UI-поток
                ExecuteOnUI(() => StatusText = status);

            // Подписываемся на событие получения обновленного списка пользователей от сервера
            _client.OnUsersListReceived += usersList => ExecuteOnUI(() =>
            {
                // Временно сохраняем ID текущего выбранного пользователя (или null), чтобы выбор не сбросился
                int? previouslySelectedId = SelectedUser?.Id;

                // Полностью очищаем UI-список пользователей, чтобы перерисовать его с нуля без дубликатов
                Users.Clear();

                // Снова возвращаем пункт общего чата на самую верхнюю строчку списка
                Users.Add(_allUsersItem);

                // Проходим циклом по пришедшему списку, исключая из него самого себя (своё имя)
                foreach (var user in usersList.Where(u => u.Name != UserName))
                {
                    // Добавляем доступных собеседников в коллекцию для отображения на экране
                    Users.Add(user);
                }

                // Разблокируем интерфейс для работы, так как соединение успешно установлено и данные получены
                IsNotConnected = false;

                // Проверяем, был ли сохранен ID выбранного ранее собеседника
                if (previouslySelectedId.HasValue)
                {
                    // Ищем этого пользователя в новом списке по ID. Если он вышел — по умолчанию выбираем общий чат
                    SelectedUser = Users.FirstOrDefault(u => u.Id == previouslySelectedId.Value) ?? _allUsersItem;
                }
            });

            // Подписка на событие получения сообщения из общего чата (трансляция для всех)
            _client.OnBroadcastMessageReceived += (sender, text) => ExecuteOnUI(() =>
            {
                // Формируем строку сообщения в красивом виде: [ИмяОтправителя]: ТекстСообщения
                string formattedMessage = $"[{sender}]: {text}";

                // Вызываем вспомогательный метод и сохраняем эту строку в историю общего чата (ID = 0)
                SaveMessageToHistory(0, formattedMessage);

                // Проверяем, открыт ли у пользователя в данный момент общий чат на экране
                if (SelectedUser?.Id == 0)
                {
                    // Если открыт — мгновенно добавляем строку в UI-коллекцию для отображения
                    ChatMessages.Add(formattedMessage);
                }
                // Если открыт приватный чат с кем-то, но автором сообщения является "СЕРВЕР"
                else if (sender == "СЕРВЕР")
                {
                    // Вариант А: Прерываем работу пользователя и показываем модальное окно уведомления от операционной системы
                    MessageBox.Show(text, "Системное уведомление сервера", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Вариант Б (альтернативный): Закомментированный код для вывода сообщения прямо в текущий открытый приватный чат
                    // ChatMessages.Add($"[СИСТЕМА]: {text}");
                }
            });

            // Подписка на событие получения приватного (личного) сообщения, защищенного сквозным шифрованием (E2EE)
            _client.OnPrivateMessageReceived += (senderId, senderName, text) => ExecuteOnUI(() =>
            {
                // Формируем строку приватного сообщения в формате: [ИмяОтправителя]: ТекстСообщения
                string formattedMessage = $"[{senderName}]: {text}";

                // Сохраняем сообщение в историю чата, привязывая его к ID отправителя (чтобы не потерялось)
                SaveMessageToHistory(senderId, formattedMessage);

                // Проверяем, открыт ли сейчас на экране чат именно с тем человеком, который прислал сообщение
                if (SelectedUser?.Id == senderId)
                {
                    // Если открыт — сразу выводим текст сообщения на экран в ленту
                    ChatMessages.Add(formattedMessage);
                }
                else
                {
                    // Если пользователь общается с другим человеком — выводим всплывающее окно-нотификацию о новом ЛС
                    MessageBox.Show($"Новое личное сообщение от {senderName}!", "Уведомление");
                }
            });

            // Асинхронно запускаем основной цикл удержания соединения, приема и отправки байтов сетевого клиента
            await _client.ConnectionAsync();
        }


        // ОТПРАВКА СООБЩЕНИЯ
        // Синхронный метод-обертка для безопасного вызова асинхронной отправки сообщения без блокировки потока интерфейса
        private void SendMessage() => _ = SendMessageAsync();
        // Асинхронный метод отправки сообщения
        private async Task SendMessageAsync()
        {
            // Защита от дурака: если клиент не инициализирован или в списке никто не выбран — выходим из метода
            if (_client == null || SelectedUser == null) return;

            // Объявляем переменную для хранения итоговой отформатированной строки сообщения
            string formattedMessage;

            // Проверяем, выбран ли сейчас на экране общий чат (ID которого равен 0)
            if (SelectedUser.Id == 0)
            {
                // Асинхронно отправляем текстовый пакет через сетевой клиент всем участникам чата
                await _client.SendToAllAsync(MessageText);

                // Формируем текст для собственной ленты, чтобы видеть, что мы написали в общий чат
                formattedMessage = $"[Вы в Общий]: {MessageText}";

                // Сохраняем это сообщение в локальную историю общего чата (ID = 0)
                SaveMessageToHistory(0, formattedMessage);

                // Добавляем сообщение в коллекцию на UI, чтобы оно отобразилось на экране у самого отправляющего
                ChatMessages.Add(formattedMessage);
            }
            else
            {
                // Если выбран конкретный пользователь — асинхронно отправляем шифрованный пакет лично ему по ID
                await _client.SendPrivateAsync(SelectedUser, MessageText);

                // Формируем текст для своей ленты с пометкой, кому именно ушло сообщение
                formattedMessage = $"[Вы для {SelectedUser.Name}]: {MessageText}";

                // Сохраняем текст в историю переписки с этим конкретным пользователем по его ID
                SaveMessageToHistory(SelectedUser.Id, formattedMessage);

                // Выводим отправленное сообщение на экран
                ChatMessages.Add(formattedMessage);
            }

            // Очищаем текстовое поле ввода (TextBox) на интерфейсе, подготавливая его для нового сообщения
            MessageText = string.Empty;
        }

        // СОХРАНЕНИЕ СООБЩЕНИЙ
        private void SaveMessageToHistory(int chatId, string message)
        {
            // Если в словаре еще нет записи (истории) для данного ID чата
            if (!_chatHistories.ContainsKey(chatId))
            {
                // Создаем для этого ID новый пустой список строк
                _chatHistories[chatId] = new List<string>();
            }
            // Добавляем новое отформатированное сообщение в список историй этого чата
            _chatHistories[chatId].Add(message);
        }

        // ПЕРЕЗАГРУЗКА СООБЩЕНИЙ НА ЭКРАНЕ (при переключении между пользователями)
        private void LoadChatHistory()
        {
            // Полностью очищаем видимую на экране ленту сообщений
            ChatMessages.Clear();

            // Если по какой-то причине текущий выбранный пользователь null — прекращаем работу
            if (SelectedUser == null) return;

            // Добавляем в начало списка сервисное локальное уведомление о переключении комнаты
            ChatMessages.Add($"[СИСТЕМА] Вы переключились на чат: {SelectedUser.Name}");

            // Пытаемся достать из словаря список сообщений для ID выбранного пользователя/комнаты
            if (_chatHistories.TryGetValue(SelectedUser.Id, out var history))
            {
                // Если история существует — проходим циклом по всем сохраненным строкам
                foreach (var msg in history)
                {
                    // Поочередно добавляем каждое сообщение в UI-коллекцию для вывода на экран
                    ChatMessages.Add(msg);
                }
            }
        }

        // Вспомогательный метод для безопасного перенаправления действий из фоновых потоков в поток интерфейса (UI Thread)
        private void ExecuteOnUI(Action action)
        {
            // Берем Диспетчер текущего WPF-приложения и синхронно выполняем переданный код (Action) в главном потоке
            Application.Current.Dispatcher.Invoke(action);
        }

        // Стандартное событие интерфейса INotifyPropertyChanged, на которое подписывается WPF для отслеживания изменений свойств
        public event PropertyChangedEventHandler? PropertyChanged;

        // Защищенный метод для вызова события PropertyChanged; CallerMemberName автоматически подставляет имя свойства, откуда его вызвали
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            // Если на событие кто-то подписан (WPF Binding) — генерируем его, передавая имя изменившегося свойства
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}