using System.Collections.Specialized;
using System.Windows;


namespace TcpChat.Client
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var vm = new MainViewModel();
            DataContext = vm;

            // Автопрокрутка при добавлении сообщения
            vm.ChatMessages.CollectionChanged += ChatMessages_CollectionChanged;
        }

        private void ChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                // Прокрутка к последнему добавленному элементу
                var last = e.NewItems[e.NewItems.Count - 1];
                // Используем Dispatcher чтобы подождать рендер
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    MessagesListBox.ScrollIntoView(last);
                }));
            }
        }
    }
}
