using System.Runtime.Serialization;

namespace Client.DTO;

public sealed class LogMessage
{
    [DataMember(Name = "type")]
    public string? Type { get; set; }   
}