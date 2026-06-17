using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using TcpServer.Core;
public class TcpNetworkServer
{
    private readonly string? _ip;
    private readonly int _port;
    private TcpListener? _listener;
    private bool _isRunning = true;
    private DbOperations? _dbOperations;
    private readonly ConcurrentDictionary<int, ConnectedClient> _connectedClients = new();
    public TcpNetworkServer(string _ip, int _port) => (this._ip, this._port) = (_ip, _port);
    public void StopListen()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _listener?.Stop();
        _connectedClients.Clear();
        Console.WriteLine("Сервер успешно остановлен.");
    }
    public async Task StartListenAsync()
    {
        if (!IPAddress.TryParse(_ip, out var ipAddress)) throw new ArgumentException("Некорректный IP-адрес");
        // Добавить проверку порта
        try
        {
            _listener = new TcpListener(ipAddress, _port);
            _dbOperations = new DbOperations();
            await _dbOperations.DbTestAsync();
            _listener.Start();
            Console.WriteLine($"[СЕРВЕР] Запущен и слушает порт {_listener.LocalEndpoint}...");
            while (_isRunning)
            {
                Console.WriteLine("[СЕРВЕР] Ожидание нового подключения...");
                TcpClient tcpClient = await _listener.AcceptTcpClientAsync();
                _ = Task.Run(async () =>
                {
                    HandleClient _handleClient = new(tcpClient, _dbOperations, _connectedClients);
                    await _handleClient.HandleClientAsync();
                });
            }
        }
        catch (ObjectDisposedException) when (!_isRunning) { Console.WriteLine("[СЕРВЕР] Работа сервера завершена."); }
        catch (SocketException ex) { Console.WriteLine($"[СЕРВЕР] Ошибка сокета: {ex.Message}"); }
    }
}
