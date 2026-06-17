namespace TcpServer.Core;
public class NetworkPacket
{
    // 1. Команда, которая определяет, что нужно сделать.
    // Возможные варианты: "CONNECT", "MESSAGE_TO_ALL", "MESSAGE_PRIVATE", "GET_USERS"
    public string Command { get; set; } = string.Empty;

    // 2. Имя отправителя или текст авторизации (используется при первом подключении)
    public string SenderName { get; set; } = string.Empty;

    // 3. ID получателя (User.Id из базы данных). 
    // Если отправляем всем (в общий чат), здесь будет 0.
    public int TargetUserId { get; set; }

    // 4. Текст самого сообщения
    public string MessageText { get; set; } = string.Empty;
}
