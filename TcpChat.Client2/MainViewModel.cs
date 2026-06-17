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
        private TcpNetworkClient? _client;
        private User? _selectedUser;
        private string _messageText = string.Empty;
        private string _userName = "User_" + new Random().Next(100, 999);
        private string _statusText = "Отключен";
        private bool _isNotConnected = true;

        // Коллекции, которые WPF будет автоматически отображать на экране
        public ObservableCollection<User> Users { get; set; } = new();
        public ObservableCollection<string> ChatMessages { get; set; } = new();

        // Специальный объект для выбора "Все пользователи"
        private readonly User _allUsersItem = new User { Id = 0, Name = "--- ОБЩИЙ ЧАТ ---" };

        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsNotConnected
        {
            get => _isNotConnected;
            set { _isNotConnected = value; OnPropertyChanged(); }
        }

        public User? SelectedUser
        {
            get => _selectedUser;
            set
            {
                _selectedUser = value;
                OnPropertyChanged();
                // При смене пользователя очищаем экран под историю конкретного диалога
                ChatMessages.Clear();
                ChatMessages.Add($"[СИСТЕМА] Вы переключились на чат: {value?.Name}");
            }
        }

        public string MessageText
        {
            get => _messageText;
            set { _messageText = value; OnPropertyChanged(); }
        }

        // Команды для кнопок
        public ICommand ConnectCommand { get; }
        public ICommand SendCommand { get; }

        public MainViewModel()
        {
            // Изменили на синхронный вызов методов, запускающих фоновые Task, чтобы XAML-компилятор не ругался
            ConnectCommand = new RelayCommand(_ => Connect());
            SendCommand = new RelayCommand(_ => SendMessage(), _ => !string.IsNullOrWhiteSpace(MessageText));

            // Сразу добавляем Общий чат в список контактов
            Users.Add(_allUsersItem);
            SelectedUser = _allUsersItem;
        }

        // Синхронные обертки-запускаторы для асинхронных команд
        private void Connect()
        {
            _ = ConnectAsync();
        }

        private void SendMessage()
        {
            _ = SendMessageAsync();
        }

        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(UserName)) return;

            StatusText = "Подключение...";
            _client = new TcpNetworkClient("127.0.0.1", 8888, UserName);

            // Подписываемся на события сетевого клиента
            _client.OnConnectionStatusChanged += status =>
                ExecuteOnUI(() => StatusText = status);

            _client.OnUsersListReceived += usersList => ExecuteOnUI(() =>
            {
                Users.Clear();
                Users.Add(_allUsersItem);
                foreach (var user in usersList.Where(u => u.Name != UserName))
                {
                    Users.Add(user);
                }
                IsNotConnected = false;
            });

            _client.OnBroadcastMessageReceived += (sender, text) => ExecuteOnUI(() =>
            {
                // Если сейчас выбран Общий чат, выводим сообщение на экран
                if (SelectedUser?.Id == 0)
                {
                    ChatMessages.Add($"[{sender}]: {text}");
                }
            });

            _client.OnPrivateMessageReceived += (senderId, senderName, text) => ExecuteOnUI(() =>
            {
                // Если у нас открыт чат именно с этим отправителем
                if (SelectedUser?.Id == senderId)
                {
                    ChatMessages.Add($"[{senderName}]: {text}");
                }
                else
                {
                    // Если открыт другой чат (или общий), уведомляем пользователя
                    MessageBox.Show($"Новое личное сообщение от {senderName}!\n[{senderName}]: {text}", "Уведомление");
                }
            });

            await _client.ConnectionAsync();
        }

        private async Task SendMessageAsync()
        {
            if (_client == null || SelectedUser == null) return;

            if (SelectedUser.Id == 0)
            {
                // Отправка в общий чат (без шифрования, как и договаривались)
                await _client.SendToAllAsync(MessageText);
                ChatMessages.Add($"[Вы в Общий]: {MessageText}");
            }
            else
            {
                // ОТПРАВКА С E2EE ШИФРОВАНИЕМ:
                // Передаем объект SelectedUser целиком, чтобы клиент мог забрать из него PublicKeyBase64
                await _client.SendPrivateAsync(SelectedUser, MessageText);
                ChatMessages.Add($"[Вы для {SelectedUser.Name}]: {MessageText}");
            }

            MessageText = string.Empty;
        }


        // Помощник для безопасного обновления UI из фонового потока сети
        private void ExecuteOnUI(Action action)
        {
            Application.Current.Dispatcher.Invoke(action);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Простая реализация ICommand для MVVM кнопок
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;
        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) =>
            (_execute, _canExecute) = (execute, canExecute);
        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    }
}
