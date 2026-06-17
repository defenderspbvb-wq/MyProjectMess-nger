using System.ComponentModel.DataAnnotations.Schema;

namespace TcpServer.Core;
// Модель, описывающая данные
public class User
{
    public int Id { get; set; }
    public string? Name { get; set; }
    [NotMapped] // Указывает EF Core, что этой колонки нет в БД и её не нужно запрашивать
    public string? PublicKeyBase64 { get; set; }
}