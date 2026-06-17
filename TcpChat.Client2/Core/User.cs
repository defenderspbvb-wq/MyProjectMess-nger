using System.ComponentModel.DataAnnotations.Schema;

namespace TcpChat.Client.Core;

public class User
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? PublicKeyBase64 { get; set; }
}
