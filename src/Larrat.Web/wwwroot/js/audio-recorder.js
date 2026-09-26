/**
 * Larrat Audio Recorder
 * Usa MediaRecorder API para gravar áudio in-app e enviar como arquivo.
 */

const LarratAudioRecorder = (() => {
    let mediaRecorder = null;
    let audioChunks = [];
    let startTime = null;
    let timerInterval = null;

    async function start() {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        audioChunks = [];
        startTime = Date.now();

        // Prefer opus/webm, fallback para outros formatos
        const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
            ? 'audio/webm;codecs=opus'
            : MediaRecorder.isTypeSupported('audio/ogg;codecs=opus')
                ? 'audio/ogg;codecs=opus'
                : 'audio/webm';

        mediaRecorder = new MediaRecorder(stream, { mimeType });
        mediaRecorder.ondataavailable = e => {
            if (e.data.size > 0) audioChunks.push(e.data);
        };
        mediaRecorder.start(100); // captura a cada 100ms

        return {
            mimeType,
            getElapsedSeconds: () => Math.floor((Date.now() - startTime) / 1000)
        };
    }

    function stop() {
        return new Promise((resolve) => {
            if (!mediaRecorder) { resolve(null); return; }

            mediaRecorder.onstop = () => {
                const blob = new Blob(audioChunks, { type: mediaRecorder.mimeType });
                const duration = (Date.now() - startTime) / 1000;
                mediaRecorder.stream.getTracks().forEach(t => t.stop());
                mediaRecorder = null;
                audioChunks = [];
                resolve({ blob, duration });
            };

            mediaRecorder.stop();
        });
    }

    function cancel() {
        if (!mediaRecorder) return;
        mediaRecorder.stream.getTracks().forEach(t => t.stop());
        mediaRecorder = null;
        audioChunks = [];
    }

    function isRecording() {
        return mediaRecorder?.state === 'recording';
    }

    // Converte blob para File para upload
    function blobToFile(blob, filename = 'audio.webm') {
        return new File([blob], filename, { type: blob.type });
    }

    return { start, stop, cancel, isRecording, blobToFile };
})();

window.LarratAudioRecorder = LarratAudioRecorder;

/**
 * Utilitários de criptografia AES-256-GCM no browser.
 * Espelha o EncryptionService do servidor.
 */
const LarratCrypto = (() => {
    async function deriveKey(secret, conversationId) {
        const encoder = new TextEncoder();
        const keyMaterial = await crypto.subtle.importKey(
            'raw',
            encoder.encode(secret),
            { name: 'HKDF' },
            false,
            ['deriveKey']
        );

        const info = encoder.encode(`larrat-chat-${conversationId}`);
        const salt = await crypto.subtle.digest('SHA-256', encoder.encode('larrat-salt-v1'));

        return await crypto.subtle.deriveKey(
            { name: 'HKDF', hash: 'SHA-256', salt, info },
            keyMaterial,
            { name: 'AES-GCM', length: 256 },
            false,
            ['encrypt', 'decrypt']
        );
    }

    async function encrypt(plainText, key) {
        const encoder = new TextEncoder();
        const iv = crypto.getRandomValues(new Uint8Array(12)); // 96-bit IV para GCM

        const cipherBuffer = await crypto.subtle.encrypt(
            { name: 'AES-GCM', iv, tagLength: 128 },
            key,
            encoder.encode(plainText)
        );

        // GCM retorna ciphertext + authTag concatenados
        const cipherArray = new Uint8Array(cipherBuffer);
        const cipherText = cipherArray.slice(0, cipherArray.length - 16);
        const authTag = cipherArray.slice(-16);

        return {
            cipherText: btoa(String.fromCharCode(...cipherText)),
            iv: btoa(String.fromCharCode(...iv)),
            authTag: btoa(String.fromCharCode(...authTag))
        };
    }

    async function decrypt(cipherTextB64, ivB64, authTagB64, key) {
        const cipherBytes = Uint8Array.from(atob(cipherTextB64), c => c.charCodeAt(0));
        const iv = Uint8Array.from(atob(ivB64), c => c.charCodeAt(0));
        const authTag = Uint8Array.from(atob(authTagB64), c => c.charCodeAt(0));

        // Concatena ciphertext + authTag (formato SubtleCrypto)
        const combined = new Uint8Array(cipherBytes.length + authTag.length);
        combined.set(cipherBytes);
        combined.set(authTag, cipherBytes.length);

        const plainBuffer = await crypto.subtle.decrypt(
            { name: 'AES-GCM', iv, tagLength: 128 },
            key,
            combined
        );

        return new TextDecoder().decode(plainBuffer);
    }

    return { deriveKey, encrypt, decrypt };
})();

window.LarratCrypto = LarratCrypto;

/**
 * Interop para Blazor: funções chamadas via JSRuntime.InvokeAsync
 */
window.LarratInterop = {
    // Foca em elemento
    focusElement: (elementId) => {
        document.getElementById(elementId)?.focus();
    },

    // Rola chat para baixo
    scrollToBottom: (elementId) => {
        const el = document.getElementById(elementId);
        if (el) el.scrollTop = el.scrollHeight;
    },

    // Copia texto para clipboard
    copyToClipboard: async (text) => {
        await navigator.clipboard.writeText(text);
    },

    // Formata tamanho de arquivo
    formatFileSize: (bytes) => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
        return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`;
    },

    // Inicia gravação de áudio
    startRecording: () => LarratAudioRecorder.start(),

    // Para e retorna blob
    stopRecording: () => LarratAudioRecorder.stop(),

    // Cancela gravação
    cancelRecording: () => LarratAudioRecorder.cancel(),

    isRecording: () => LarratAudioRecorder.isRecording(),

    // WebRTC
    initWebRTC: (connection) => LarratWebRTC.init(connection),
    joinVoiceRoom: (roomId, withVideo) => LarratWebRTC.joinRoom(roomId, withVideo),
    leaveVoiceRoom: () => LarratWebRTC.leaveRoom(),
    toggleMute: () => LarratWebRTC.toggleMute(),
    toggleVideo: () => LarratWebRTC.toggleVideo(),
    startScreenShare: () => LarratWebRTC.startScreenShare(),
    stopScreenShare: () => LarratWebRTC.stopScreenShare(),
    createSubRoom: (name) => LarratWebRTC.createSubRoom(name),
};
