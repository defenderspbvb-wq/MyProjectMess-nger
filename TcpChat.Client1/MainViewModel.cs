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
        private TcpNetworkClient? _client;
        private User? _selectedUser;
        private string _messageText = string.Empty;
        private string _userName = "User_" + new Random().Next(100, 999);
        private string _statusText = "Отключен";
        private bool _isNotConnected = true;

        // Хранилище истории сообщений: Key = ID Пользователя (0 для общего чата), Value = Список сообщений
        private readonly Dictionary<int, List<string>> _chatHistories = new();

        public ObservableCollection<User> Users { get; set; } = new();
        public ObservableCollection<string> ChatMessages { get; set; } = new();

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
                
                // Переключаем историю сообщений на экране
                LoadChatHistory();
            }
        }

        public string MessageText
        {
            get => _messageText;
            set { _messageText = value; OnPropertyChanged(); }
        }

        public ICommand ConnectCommand { get; }
        public ICommand SendCommand { get; }

        public MainViewModel()
        {
            ConnectCommand = new RelayCommand(_ => Connect());
            SendCommand = new RelayCommand(_ => SendMessage(), _ => !string.IsNullOrWhiteSpace(MessageText));

            Users.Add(_allUsersItem);
            SelectedUser = _allUsersItem;
        }

        private void Connect() => _ = ConnectAsync();
        private void SendMessage() => _ = SendMessageAsync();

        private async Task ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(UserName)) return;

            StatusText = "Подключение...";
            _client = new TcpNetworkClient("127.0.0.1", 8888, UserName);

            _client.OnConnectionStatusChanged += status =>
                ExecuteOnUI(() => StatusText = status);

            _client.OnUsersListReceived += usersList => ExecuteOnUI(() =>
            {
                // Запоминаем, какой ID был выбран (например, 0)
                int? previouslySelectedId = SelectedUser?.Id;

                Users.Clear();
                Users.Add(_allUsersItem);
                foreach (var user in usersList.Where(u => u.Name != UserName))
                {
                    Users.Add(user);
                }
                IsNotConnected = false;

                // СРАЗУ ЖЕ восстанавливаем выбор, чтобы SelectedUser не оставался null!
                if (previouslySelectedId.HasValue)
                {
                    SelectedUser = Users.FirstOrDefault(u => u.Id == previouslySelectedId.Value) ?? _allUsersItem;
                }
            });


            // Обработка общего чата
            _client.OnBroadcastMessageReceived += (sender, text) => ExecuteOnUI(() =>
            {
                string formattedMessage = $"[{sender}]: {text}";
                SaveMessageToHistory(0, formattedMessage); // Сохраняем в историю общего чата

                // Если открыт общий чат — просто выводим на экран
                if (SelectedUser?.Id == 0)
                {
                    ChatMessages.Add(formattedMessage);
                }
                // Если открыт приватный чат, но это важное системное уведомление от СЕРВЕРА
                else if (sender == "СЕРВЕР")
                {
                    // Вариант А: Показать MessageBox (будет всплывать окно)
                    MessageBox.Show(text, "Системное уведомление сервера", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Вариант Б (альтернативный): Дублировать системное сообщение прямо в текущий приватный чат, чтобы не отвлекать окнами
                    // ChatMessages.Add($"[СИСТЕМА]: {text}");
                }
            });


            // Обработка приватного чата (E2EE)
            _client.OnPrivateMessageReceived += (senderId, senderName, text) => ExecuteOnUI(() =>
            {
                string formattedMessage = $"[{senderName}]: {text}";
                SaveMessageToHistory(senderId, formattedMessage);

                if (SelectedUser?.Id == senderId)
                {
                    ChatMessages.Add(formattedMessage);
                }
                else
                {
                    MessageBox.Show($"Новое личное сообщение от {senderName}!", "Уведомление");
                }
            });

            await _client.ConnectionAsync();
        }

        private async Task SendMessageAsync()
        {
            if (_client == null || SelectedUser == null) return;

            string formattedMessage;
            if (SelectedUser.Id == 0)
            {
                await _client.SendToAllAsync(MessageText);
                formattedMessage = $"[Вы в Общий]: {MessageText}";
                SaveMessageToHistory(0, formattedMessage);
                ChatMessages.Add(formattedMessage);
            }
            else
            {
                await _client.SendPrivateAsync(SelectedUser, MessageText);
                formattedMessage = $"[Вы для {SelectedUser.Name}]: {MessageText}";
                SaveMessageToHistory(SelectedUser.Id, formattedMessage);
                ChatMessages.Add(formattedMessage);
            }

            MessageText = string.Empty;
        }

        // Вспомогательные методы для работы с историей в памяти клиента
        private void SaveMessageToHistory(int chatId, string message)
        {
            if (!_chatHistories.ContainsKey(chatId))
            {
                _chatHistories[chatId] = new List<string>();
            }
            _chatHistories[chatId].Add(message);
        }

        private void LoadChatHistory()
        {
            ChatMessages.Clear();
            if (SelectedUser == null) return;

            ChatMessages.Add($"[СИСТЕМА] Вы переключились на чат: {SelectedUser.Name}");

            if (_chatHistories.TryGetValue(SelectedUser.Id, out var history))
            {
                foreach (var msg in history)
                {
                    ChatMessages.Add(msg);
                }
            }
        }

        private void ExecuteOnUI(Action action)
        {
            Application.Current.Dispatcher.Invoke(action);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
