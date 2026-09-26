namespace Larrat.Core.Entities;

public class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsPrivate { get; set; } = false;
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? InviteCode { get; set; }

    // Navegação
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<Channel> Channels { get; set; } = new List<Channel>();
    public ICollection<VoiceRoom> VoiceRooms { get; set; } = new List<VoiceRoom>();
}

public class GroupMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public GroupRole Role { get; set; } = GroupRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public string? Nickname { get; set; }
}

public enum GroupRole
{
    Owner,
    Admin,
    Member
}
