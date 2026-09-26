using Larrat.Core.Data;
using Larrat.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Larrat.Core.Services;

public interface IGroupService
{
    Task<Group> CreateGroupAsync(string ownerId, string name, string? description = null);
    Task<Channel> AddChannelAsync(Guid groupId, string name, string requestingUserId);
    Task<VoiceRoom> AddVoiceRoomAsync(Guid groupId, string name, string requestingUserId);
    Task<VoiceSubRoom> CreateSubRoomAsync(Guid voiceRoomId, string name, string creatorUserId);
    Task<bool> JoinGroupByInviteAsync(string userId, string inviteCode);
    Task<bool> AddMemberAsync(Guid groupId, string targetUserId, string requestingUserId, GroupRole role = GroupRole.Member);
    Task<bool> RemoveMemberAsync(Guid groupId, string targetUserId, string requestingUserId);
    Task<List<Group>> GetUserGroupsAsync(string userId);
    Task<Group?> GetGroupWithDetailsAsync(Guid groupId);
    Task<string> RegenerateInviteCodeAsync(Guid groupId, string requestingUserId);
    Task<bool> JoinVoiceRoomAsync(Guid voiceRoomId, string userId);
    Task<bool> LeaveVoiceRoomAsync(Guid voiceRoomId, string userId);
}

public class GroupService : IGroupService
{
    private readonly LarratDbContext _db;

    public GroupService(LarratDbContext db)
    {
        _db = db;
    }

    public async Task<Group> CreateGroupAsync(string ownerId, string name, string? description = null)
    {
        var group = new Group
        {
            Name = name,
            Description = description,
            OwnerId = ownerId,
            InviteCode = GenerateInviteCode()
        };

        // Canal padrão #geral
        var defaultChannel = new Channel
        {
            Name = "geral",
            IsDefault = true,
            Position = 0
        };
        group.Channels.Add(defaultChannel);

        // VoiceRoom padrão
        var defaultVoice = new VoiceRoom
        {
            Name = "Voz Geral",
            Position = 0
        };
        group.VoiceRooms.Add(defaultVoice);

        // Adiciona o criador como Owner
        group.Members.Add(new GroupMember
        {
            UserId = ownerId,
            Role = GroupRole.Owner
        });

        _db.Groups.Add(group);
        await _db.SaveChangesAsync();
        return group;
    }

    public async Task<Channel> AddChannelAsync(Guid groupId, string name, string requestingUserId)
    {
        await AssertAdminAsync(groupId, requestingUserId);

        var maxPos = await _db.Channels
            .Where(c => c.GroupId == groupId)
            .MaxAsync(c => (int?)c.Position) ?? -1;

        var channel = new Channel
        {
            GroupId = groupId,
            Name = name,
            Position = maxPos + 1
        };

        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();
        return channel;
    }

    public async Task<VoiceRoom> AddVoiceRoomAsync(Guid groupId, string name, string requestingUserId)
    {
        await AssertAdminAsync(groupId, requestingUserId);

        var maxPos = await _db.VoiceRooms
            .Where(v => v.GroupId == groupId)
            .MaxAsync(v => (int?)v.Position) ?? -1;

        var room = new VoiceRoom
        {
            GroupId = groupId,
            Name = name,
            Position = maxPos + 1
        };

        _db.VoiceRooms.Add(room);
        await _db.SaveChangesAsync();
        return room;
    }

    public async Task<VoiceSubRoom> CreateSubRoomAsync(Guid voiceRoomId, string name, string creatorUserId)
    {
        var subRoom = new VoiceSubRoom
        {
            ParentVoiceRoomId = voiceRoomId,
            Name = name,
            CreatedByUserId = creatorUserId,
            IsTemporary = true
        };

        _db.VoiceSubRooms.Add(subRoom);
        await _db.SaveChangesAsync();
        return subRoom;
    }

    public async Task<bool> JoinGroupByInviteAsync(string userId, string inviteCode)
    {
        var group = await _db.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.InviteCode == inviteCode);

        if (group == null) return false;
        if (group.Members.Any(m => m.UserId == userId)) return true; // Já é membro

        group.Members.Add(new GroupMember { UserId = userId, Role = GroupRole.Member });
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AddMemberAsync(Guid groupId, string targetUserId, string requestingUserId, GroupRole role = GroupRole.Member)
    {
        await AssertAdminAsync(groupId, requestingUserId);

        if (await _db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == targetUserId))
            return false;

        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = targetUserId, Role = role });
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveMemberAsync(Guid groupId, string targetUserId, string requestingUserId)
    {
        var member = await _db.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == targetUserId);
        if (member == null) return false;

        // Somente admins/dono podem remover; usuário pode sair sozinho
        if (requestingUserId != targetUserId)
            await AssertAdminAsync(groupId, requestingUserId);

        _db.GroupMembers.Remove(member);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<Group>> GetUserGroupsAsync(string userId)
    {
        return await _db.Groups
            .Where(g => g.Members.Any(m => m.UserId == userId))
            .Include(g => g.Channels.OrderBy(c => c.Position))
            .Include(g => g.VoiceRooms.OrderBy(v => v.Position))
                .ThenInclude(v => v.Participants)
                    .ThenInclude(p => p.User)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<Group?> GetGroupWithDetailsAsync(Guid groupId)
    {
        return await _db.Groups
            .Include(g => g.Owner)
            .Include(g => g.Members).ThenInclude(m => m.User)
            .Include(g => g.Channels.OrderBy(c => c.Position))
            .Include(g => g.VoiceRooms.OrderBy(v => v.Position))
                .ThenInclude(v => v.Participants).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(g => g.Id == groupId);
    }

    public async Task<string> RegenerateInviteCodeAsync(Guid groupId, string requestingUserId)
    {
        await AssertAdminAsync(groupId, requestingUserId);
        var group = await _db.Groups.FindAsync(groupId)
            ?? throw new InvalidOperationException("Grupo não encontrado.");
        group.InviteCode = GenerateInviteCode();
        await _db.SaveChangesAsync();
        return group.InviteCode;
    }

    public async Task<bool> JoinVoiceRoomAsync(Guid voiceRoomId, string userId)
    {
        // Remove de outros rooms primeiro
        var existing = await _db.VoiceRoomParticipants
            .Where(p => p.UserId == userId)
            .ToListAsync();
        _db.VoiceRoomParticipants.RemoveRange(existing);

        _db.VoiceRoomParticipants.Add(new VoiceRoomParticipant
        {
            VoiceRoomId = voiceRoomId,
            UserId = userId
        });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> LeaveVoiceRoomAsync(Guid voiceRoomId, string userId)
    {
        var participant = await _db.VoiceRoomParticipants
            .FirstOrDefaultAsync(p => p.VoiceRoomId == voiceRoomId && p.UserId == userId);
        if (participant == null) return false;

        _db.VoiceRoomParticipants.Remove(participant);

        // Limpa sub-rooms temporárias vazias
        var subRooms = await _db.VoiceSubRooms
            .Where(sr => sr.ParentVoiceRoomId == voiceRoomId && sr.IsTemporary)
            .ToListAsync();
        // (limpeza completa é feita via SignalR Hub)

        await _db.SaveChangesAsync();
        return true;
    }

    // Helpers
    private async Task AssertAdminAsync(Guid groupId, string userId)
    {
        var member = await _db.GroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId);

        if (member == null || member.Role == GroupRole.Member)
            throw new UnauthorizedAccessException("Apenas admins ou o dono do grupo podem realizar esta ação.");
    }

    private static string GenerateInviteCode()
    {
        // Código de convite tipo "ABC123XY" - 8 chars alfanumérico
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
    }
}
