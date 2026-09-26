namespace Larrat.Core.Entities;

/// <summary>
/// Canal de texto dentro de um Grupo (como #geral, #avisos, etc.)
/// </summary>
public class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }
    public int Position { get; set; } = 0;
    public bool IsDefault { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navegação
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

/// <summary>
/// Sala de voz/vídeo dentro de um Grupo (estilo Discord)
/// </summary>
public class VoiceRoom
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }
    public int Position { get; set; } = 0;
    public int MaxParticipants { get; set; } = 25;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Participantes ativos (em memória via SignalR, não persiste)
    public ICollection<VoiceRoomParticipant> Participants { get; set; } = new List<VoiceRoomParticipant>();
}

public class VoiceRoomParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VoiceRoomId { get; set; }
    public VoiceRoom? VoiceRoom { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public bool IsMuted { get; set; } = false;
    public bool IsVideoEnabled { get; set; } = false;
    public bool IsScreenSharing { get; set; } = false;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Sub-grupo de voz criado dinamicamente dentro de uma VoiceRoom
/// </summary>
public class VoiceSubRoom
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid ParentVoiceRoomId { get; set; }
    public VoiceRoom? ParentVoiceRoom { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsTemporary { get; set; } = true; // Desaparece quando todos saem
}
