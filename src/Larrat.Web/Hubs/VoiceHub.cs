using Larrat.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Larrat.Web.Hubs;

/// <summary>
/// Hub de sinalização WebRTC para chamadas de voz/vídeo em grupo.
/// Coordena offer/answer/ICE candidate entre peers.
/// </summary>
[Authorize]
public class VoiceHub : Hub
{
    private readonly IGroupService _groupService;

    // roomId -> set de userId
    private static readonly Dictionary<string, HashSet<string>> _roomParticipants = new();
    // userId -> connectionId (VoiceHub)
    private static readonly Dictionary<string, string> _userVoiceConnections = new();
    private static readonly object _lock = new();

    public VoiceHub(IGroupService groupService)
    {
        _groupService = groupService;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        await LeaveCurrentRoom(userId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Entra em uma voice room. Notifica todos os participantes existentes
    /// para iniciar a negociação WebRTC com o novo participante.
    /// </summary>
    public async Task JoinVoiceRoom(string voiceRoomId)
    {
        var userId = GetUserId();

        // Sai do room atual se estiver em algum
        await LeaveCurrentRoom(userId);

        List<string> existingParticipants;
        lock (_lock)
        {
            if (!_roomParticipants.ContainsKey(voiceRoomId))
                _roomParticipants[voiceRoomId] = new HashSet<string>();

            existingParticipants = _roomParticipants[voiceRoomId].ToList();
            _roomParticipants[voiceRoomId].Add(userId);
            _userVoiceConnections[userId] = Context.ConnectionId;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"voice-{voiceRoomId}");

        // Informa o novo participante quem já está na sala
        await Clients.Caller.SendAsync("RoomParticipants", voiceRoomId, existingParticipants);

        // Informa os demais que um novo participante chegou (eles iniciam o offer WebRTC)
        await Clients.OthersInGroup($"voice-{voiceRoomId}").SendAsync("ParticipantJoined", voiceRoomId, userId);

        // Persiste no DB
        await _groupService.JoinVoiceRoomAsync(Guid.Parse(voiceRoomId), userId);
    }

    /// <summary>
    /// Sai da voice room atual.
    /// </summary>
    public async Task LeaveVoiceRoom(string voiceRoomId)
    {
        var userId = GetUserId();
        await LeaveRoomInternal(voiceRoomId, userId);
    }

    // ───────── Sinalização WebRTC ─────────

    /// <summary>
    /// Envia SDP Offer para um peer específico (quem acabou de entrar na sala).
    /// </summary>
    public async Task SendOffer(string targetUserId, string voiceRoomId, string sdpOffer)
    {
        var senderConnId = Context.ConnectionId;
        var senderId = GetUserId();

        string? targetConnId;
        lock (_lock) { _userVoiceConnections.TryGetValue(targetUserId, out targetConnId); }

        if (targetConnId != null)
            await Clients.Client(targetConnId).SendAsync("ReceiveOffer", senderId, voiceRoomId, sdpOffer);
    }

    /// <summary>
    /// Envia SDP Answer em resposta a um Offer.
    /// </summary>
    public async Task SendAnswer(string targetUserId, string voiceRoomId, string sdpAnswer)
    {
        var senderId = GetUserId();
        string? targetConnId;
        lock (_lock) { _userVoiceConnections.TryGetValue(targetUserId, out targetConnId); }

        if (targetConnId != null)
            await Clients.Client(targetConnId).SendAsync("ReceiveAnswer", senderId, voiceRoomId, sdpAnswer);
    }

    /// <summary>
    /// Envia ICE Candidate para troca de conectividade peer-to-peer.
    /// </summary>
    public async Task SendIceCandidate(string targetUserId, string voiceRoomId, string candidateJson)
    {
        var senderId = GetUserId();
        string? targetConnId;
        lock (_lock) { _userVoiceConnections.TryGetValue(targetUserId, out targetConnId); }

        if (targetConnId != null)
            await Clients.Client(targetConnId).SendAsync("ReceiveIceCandidate", senderId, voiceRoomId, candidateJson);
    }

    /// <summary>
    /// Alterna mudo.
    /// </summary>
    public async Task ToggleMute(string voiceRoomId, bool isMuted)
    {
        var userId = GetUserId();
        await Clients.OthersInGroup($"voice-{voiceRoomId}").SendAsync("ParticipantMuteChanged", userId, isMuted);
    }

    /// <summary>
    /// Alterna vídeo.
    /// </summary>
    public async Task ToggleVideo(string voiceRoomId, bool isVideoEnabled)
    {
        var userId = GetUserId();
        await Clients.OthersInGroup($"voice-{voiceRoomId}").SendAsync("ParticipantVideoChanged", userId, isVideoEnabled);
    }

    /// <summary>
    /// Cria um sub-room temporário dentro de uma voice room.
    /// </summary>
    public async Task CreateSubRoom(string parentVoiceRoomId, string subRoomName)
    {
        var userId = GetUserId();
        var subRoom = await _groupService.CreateSubRoomAsync(Guid.Parse(parentVoiceRoomId), subRoomName, userId);
        await Clients.Group($"voice-{parentVoiceRoomId}").SendAsync("SubRoomCreated", new
        {
            id = subRoom.Id,
            name = subRoom.Name,
            parentVoiceRoomId,
            createdByUserId = userId
        });
    }

    // ───────── Helpers ─────────

    private async Task LeaveCurrentRoom(string userId)
    {
        string? currentRoom = null;
        lock (_lock)
        {
            foreach (var kv in _roomParticipants)
            {
                if (kv.Value.Contains(userId)) { currentRoom = kv.Key; break; }
            }
        }

        if (currentRoom != null)
            await LeaveRoomInternal(currentRoom, userId);
    }

    private async Task LeaveRoomInternal(string voiceRoomId, string userId)
    {
        lock (_lock)
        {
            if (_roomParticipants.ContainsKey(voiceRoomId))
            {
                _roomParticipants[voiceRoomId].Remove(userId);
                if (_roomParticipants[voiceRoomId].Count == 0)
                    _roomParticipants.Remove(voiceRoomId);
            }
            _userVoiceConnections.Remove(userId);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"voice-{voiceRoomId}");
        await Clients.Group($"voice-{voiceRoomId}").SendAsync("ParticipantLeft", voiceRoomId, userId);

        // Persiste no DB
        if (Guid.TryParse(voiceRoomId, out var roomGuid))
            await _groupService.LeaveVoiceRoomAsync(roomGuid, userId);
    }

    private string GetUserId()
        => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new HubException("Usuário não autenticado.");
}
