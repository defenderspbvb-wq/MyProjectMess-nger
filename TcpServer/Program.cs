internal class Program
{
    private static async Task Main(string[] args)
    {
        // Создаем экземпляр класса сервера
        TcpNetworkServer server = new TcpNetworkServer("0.0.0.0", 8888);

        // Запускаем сервер в фоновом режиме (без await, чтобы код шел дальше)
        _ = server.StartListenAsync();

        // Останавливаем сервер и освобождаем порт
        Console.WriteLine("Нажмите любую клавишу, чтобы остановить сервер...");
        Console.ReadKey();
        server.StopListen();
    }

}
