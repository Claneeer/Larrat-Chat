/**
 * Larrat WebRTC Engine
 * Gerencia conexões peer-to-peer para chamadas de voz/vídeo em grupo.
 * Integrado com SignalR VoiceHub para troca de sinalizações (offer/answer/ICE).
 */

const LarratWebRTC = (() => {
    // Configuração ICE (STUN público + TURN se disponível)
    const ICE_SERVERS = [
        { urls: 'stun:stun.l.google.com:19302' },
        { urls: 'stun:stun1.l.google.com:19302' },
        { urls: 'stun:stun.stunprotocol.org:3478' }
    ];

    let voiceHubConnection = null;
    let localStream = null;
    let screenStream = null;
    let currentRoomId = null;
    const peers = {}; // { userId: RTCPeerConnection }
    const callbacks = {};

    // ── Inicialização ──────────────────────────────────────

    async function init(signalrConnection) {
        voiceHubConnection = signalrConnection;

        // Ouve eventos de sinalização do Hub
        voiceHubConnection.on('RoomParticipants', handleRoomParticipants);
        voiceHubConnection.on('ParticipantJoined', handleParticipantJoined);
        voiceHubConnection.on('ParticipantLeft', handleParticipantLeft);
        voiceHubConnection.on('ReceiveOffer', handleOffer);
        voiceHubConnection.on('ReceiveAnswer', handleAnswer);
        voiceHubConnection.on('ReceiveIceCandidate', handleIceCandidate);
        voiceHubConnection.on('ParticipantMuteChanged', (userId, isMuted) => {
            callbacks.onMuteChanged?.(userId, isMuted);
        });
        voiceHubConnection.on('ParticipantVideoChanged', (userId, isVideoEnabled) => {
            callbacks.onVideoChanged?.(userId, isVideoEnabled);
        });
        voiceHubConnection.on('SubRoomCreated', (subRoom) => {
            callbacks.onSubRoomCreated?.(subRoom);
        });
    }

    function on(event, callback) {
        callbacks[event] = callback;
    }

    // ── Mídia Local ────────────────────────────────────────

    async function getLocalMedia(audio = true, video = false) {
        try {
            localStream = await navigator.mediaDevices.getUserMedia({ audio, video });
            callbacks.onLocalStream?.(localStream);
            return localStream;
        } catch (err) {
            console.error('[Larrat WebRTC] Erro ao acessar mídia:', err);
            throw err;
        }
    }

    async function startScreenShare() {
        try {
            screenStream = await navigator.mediaDevices.getDisplayMedia({ video: true });
            const videoTrack = screenStream.getVideoTracks()[0];

            // Substitui track de vídeo em todos os peers
            Object.values(peers).forEach(pc => {
                const sender = pc.getSenders().find(s => s.track?.kind === 'video');
                if (sender) sender.replaceTrack(videoTrack);
            });

            videoTrack.onended = stopScreenShare;
            callbacks.onScreenShare?.(screenStream);
            return screenStream;
        } catch (err) {
            console.error('[Larrat WebRTC] Erro no compartilhamento de tela:', err);
            throw err;
        }
    }

    async function stopScreenShare() {
        if (!screenStream) return;
        screenStream.getTracks().forEach(t => t.stop());
        screenStream = null;

        // Restaura câmera se disponível
        if (localStream) {
            const videoTrack = localStream.getVideoTracks()[0];
            if (videoTrack) {
                Object.values(peers).forEach(pc => {
                    const sender = pc.getSenders().find(s => s.track?.kind === 'video');
                    if (sender) sender.replaceTrack(videoTrack);
                });
            }
        }
        callbacks.onScreenShareEnded?.();
    }

    // ── Sala de Voz ────────────────────────────────────────

    async function joinRoom(roomId, withVideo = false) {
        if (currentRoomId) await leaveRoom();
        currentRoomId = roomId;

        await getLocalMedia(true, withVideo);
        await voiceHubConnection.invoke('JoinVoiceRoom', roomId);
    }

    async function leaveRoom() {
        if (!currentRoomId) return;

        // Fecha todos os peers
        Object.entries(peers).forEach(([userId, pc]) => {
            pc.close();
            delete peers[userId];
            callbacks.onPeerLeft?.(userId);
        });

        // Para stream local
        if (localStream) {
            localStream.getTracks().forEach(t => t.stop());
            localStream = null;
        }

        await voiceHubConnection.invoke('LeaveVoiceRoom', currentRoomId);
        currentRoomId = null;
    }

    async function toggleMute() {
        if (!localStream) return;
        const audioTrack = localStream.getAudioTracks()[0];
        if (!audioTrack) return;
        audioTrack.enabled = !audioTrack.enabled;
        const isMuted = !audioTrack.enabled;
        await voiceHubConnection.invoke('ToggleMute', currentRoomId, isMuted);
        callbacks.onLocalMuteChanged?.(isMuted);
        return isMuted;
    }

    async function toggleVideo() {
        if (!localStream) return;
        const videoTrack = localStream.getVideoTracks()[0];
        if (!videoTrack) return;
        videoTrack.enabled = !videoTrack.enabled;
        const isEnabled = videoTrack.enabled;
        await voiceHubConnection.invoke('ToggleVideo', currentRoomId, isEnabled);
        callbacks.onLocalVideoChanged?.(isEnabled);
        return isEnabled;
    }

    async function createSubRoom(name) {
        await voiceHubConnection.invoke('CreateSubRoom', currentRoomId, name);
    }

    // ── Handlers de Sinalização ───────────────────────────

    async function handleRoomParticipants(roomId, participantUserIds) {
        // Para cada participante existente, eu (recém chegado) inicio o offer
        for (const userId of participantUserIds) {
            if (userId) await createOfferFor(userId, roomId);
        }
    }

    async function handleParticipantJoined(roomId, userId) {
        // O novo peer irá receber o offer de todos os existentes
        // Aqui, como participante existente, nós criamos o offer para o recém chegado
        await createOfferFor(userId, roomId);
        callbacks.onPeerJoined?.(userId);
    }

    function handleParticipantLeft(roomId, userId) {
        if (peers[userId]) {
            peers[userId].close();
            delete peers[userId];
        }
        callbacks.onPeerLeft?.(userId);
    }

    async function handleOffer(senderId, roomId, sdpOffer) {
        const pc = getOrCreatePeerConnection(senderId, roomId);
        await pc.setRemoteDescription(new RTCSessionDescription({ type: 'offer', sdp: sdpOffer }));
        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        await voiceHubConnection.invoke('SendAnswer', senderId, roomId, answer.sdp);
    }

    async function handleAnswer(senderId, roomId, sdpAnswer) {
        const pc = peers[senderId];
        if (pc) await pc.setRemoteDescription(new RTCSessionDescription({ type: 'answer', sdp: sdpAnswer }));
    }

    async function handleIceCandidate(senderId, roomId, candidateJson) {
        const pc = peers[senderId];
        if (pc) {
            try {
                const candidate = JSON.parse(candidateJson);
                await pc.addIceCandidate(new RTCIceCandidate(candidate));
            } catch (e) {
                console.warn('[Larrat WebRTC] ICE candidate inválido:', e);
            }
        }
    }

    // ── Peer Connection ───────────────────────────────────

    async function createOfferFor(targetUserId, roomId) {
        const pc = getOrCreatePeerConnection(targetUserId, roomId);
        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        await voiceHubConnection.invoke('SendOffer', targetUserId, roomId, offer.sdp);
    }

    function getOrCreatePeerConnection(userId, roomId) {
        if (peers[userId]) return peers[userId];

        const pc = new RTCPeerConnection({ iceServers: ICE_SERVERS });
        peers[userId] = pc;

        // Adiciona tracks locais
        if (localStream) {
            localStream.getTracks().forEach(track => pc.addTrack(track, localStream));
        }

        // ICE Candidate -> envia via SignalR
        pc.onicecandidate = async (event) => {
            if (event.candidate) {
                await voiceHubConnection.invoke(
                    'SendIceCandidate',
                    userId,
                    roomId,
                    JSON.stringify(event.candidate)
                );
            }
        };

        // Track remoto recebido
        pc.ontrack = (event) => {
            callbacks.onRemoteStream?.(userId, event.streams[0]);
        };

        pc.onconnectionstatechange = () => {
            callbacks.onConnectionStateChanged?.(userId, pc.connectionState);
        };

        pc.oniceconnectionstatechange = () => {
            if (pc.iceConnectionState === 'failed') {
                pc.restartIce(); // Tenta reconectar
            }
        };

        return pc;
    }

    // ── Detecção de Fala (VAD simples) ───────────────────

    function startVoiceActivityDetection(callback) {
        if (!localStream) return;
        const audioCtx = new AudioContext();
        const source = audioCtx.createMediaStreamSource(localStream);
        const analyser = audioCtx.createAnalyser();
        analyser.fftSize = 512;
        source.connect(analyser);

        const dataArray = new Uint8Array(analyser.frequencyBinCount);
        const SPEAKING_THRESHOLD = 30;

        let speaking = false;
        const checkInterval = setInterval(() => {
            analyser.getByteFrequencyData(dataArray);
            const avg = dataArray.reduce((a, b) => a + b, 0) / dataArray.length;
            if (avg > SPEAKING_THRESHOLD && !speaking) {
                speaking = true;
                callback(true);
            } else if (avg <= SPEAKING_THRESHOLD && speaking) {
                speaking = false;
                callback(false);
            }
        }, 100);

        return () => {
            clearInterval(checkInterval);
            audioCtx.close();
        };
    }

    return {
        init,
        on,
        joinRoom,
        leaveRoom,
        toggleMute,
        toggleVideo,
        startScreenShare,
        stopScreenShare,
        createSubRoom,
        startVoiceActivityDetection,
        getLocalStream: () => localStream,
        getPeers: () => peers
    };
})();

window.LarratWebRTC = LarratWebRTC;
