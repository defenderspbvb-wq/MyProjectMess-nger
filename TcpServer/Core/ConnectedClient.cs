namespace TcpServer.Core;

public class ConnectedClient
{
    public int UserId { get; set; }          // ID пользователя из базы данных (для работы с БД)
    public string UserName { get; set; }      // Имя пользователя (для вывода в консоль/логи)
    public StreamWriter Writer { get; set; } // Канал связи для отправки сообщений в сеть
    public string? PublicKeyBase64 { get; set; }

    public ConnectedClient(int id, string name, StreamWriter writer)
    {
        UserId = id;
        UserName = name;
        Writer = writer;
    }
}
