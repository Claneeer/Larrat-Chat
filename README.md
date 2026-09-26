# Larrat Chat 🟡⚫

> Chat em tempo real com criptografia de ponta a ponta, chamadas de voz/vídeo estilo Discord, grupos com sub-salas, envio de arquivos até 150MB e gravação de áudio.

![Design: 70% preto / 20% amarelo / 10% branco](https://via.placeholder.com/800x100/0D0D0D/F5C518?text=Larrat+%E2%80%94+70%25+Preto+%7C+20%25+Amarelo+%7C+10%25+Branco)

## Stack Tecnológico

| Componente       | Tecnologia                          |
|-----------------|-------------------------------------|
| Backend          | ASP.NET Core 8 (Blazor Server)     |
| Banco de Dados   | PostgreSQL + EF Core 8             |
| Tempo Real       | SignalR (mensagens) + WebRTC (voz) |
| Autenticação     | JWT + Refresh Token                 |
| Criptografia     | AES-256-GCM + HKDF                 |
| Upload           | Streaming até 150MB                 |

## Funcionalidades

- ✅ **Chat em tempo real** via SignalR
- ✅ **Criptografia E2E** — AES-256-GCM (chaves derivadas via HKDF do JWT secret)
- ✅ **Chamadas de voz/vídeo** — WebRTC peer-to-peer (mesh topology)
- ✅ **Sub-grupos de voz** — criados dinamicamente dentro de salas (como Discord)
- ✅ **Grupos** com canais de texto e salas de voz
- ✅ **Gravação de áudio** in-app (MediaRecorder API)
- ✅ **Upload de arquivos** até 150MB com SHA-256 para integridade
- ✅ **Indicador de digitando**, reações a mensagens, resposta a mensagens
- ✅ **Compartilhamento de tela** via WebRTC
- ✅ **Detecção de voz ativa** (VAD)
- ✅ **Convite por código** para grupos
- ✅ **Status de usuário** (Online, Away, DND, Offline)
- ✅ **Design 70/20/10** — Preto / Amarelo Larrat (#F5C518) / Branco

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [PostgreSQL 15+](https://www.postgresql.org/download/)
- Node.js (opcional, para produção)

## Setup Rápido

### 1. Configure o banco de dados

```sql
-- No PostgreSQL (psql ou pgAdmin):
CREATE DATABASE larrat_chat;
```

### 2. Configure `appsettings.json`

Edite `src/Larrat.Web/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=larrat_chat;Username=postgres;Password=SUA_SENHA"
  },
  "JwtSettings": {
    "SecretKey": "MUDE_ISSO_EM_PRODUCAO_MINIMO_32_CARACTERES!",
    "Issuer": "https://larrat.app",
    "Audience": "https://larrat.app"
  }
}
```

> ⚠️ **Importante**: Mude o `SecretKey` para uma string aleatória forte em produção. Esta chave é usada para derivar as chaves de criptografia AES-256.

### 3. Execute as migrations

```powershell
cd d:\Projetos2\Larrat
dotnet ef database update --project src/Larrat.Core --startup-project src/Larrat.Web
```

### 4. Rode o projeto

```powershell
cd src/Larrat.Web
dotnet run
```

Acesse: **https://localhost:7xxx** (a porta será exibida no terminal)

## Arquitetura de Criptografia

```
JWT Secret Key (master)
       │
       ▼ HKDF-SHA256 (info = "larrat-chat-{conversationId}")
Conversation Key (256-bit AES)
       │
       ▼ AES-256-GCM
EncryptedContent + IV (12B) + AuthTag (16B)
       │
       ▼ Armazenado no PostgreSQL (mensagens nunca em plaintext)
```

A chave mestre nunca sai do servidor. Cada canal/conversa tem sua própria chave derivada via HKDF. A tag de autenticação GCM garante integridade — qualquer adulteração é detectada.

## Arquitetura de Voz (WebRTC)

```
Usuário A ──── ICE/SDP via SignalR VoiceHub ──── Usuário B
     │                                                │
     └──────── WebRTC P2P (áudio/vídeo direto) ──────┘
                         (sem passar pelo servidor)
```

Para grupos maiores, usa mesh topology (cada participante conecta a todos os outros). Para grupos muito grandes, pode ser migrado para SFU (ex: mediasoup).

## Estrutura do Projeto

```
src/
├── Larrat.Core/              # Domínio e infraestrutura
│   ├── Entities/             # Entidades EF Core
│   ├── Data/                 # DbContext + Migrations
│   └── Services/             # Lógica de negócio
└── Larrat.Web/               # Blazor Server + SignalR
    ├── Components/           # Componentes Razor
    ├── Controllers/          # API (upload de arquivos)
    ├── Hubs/                 # ChatHub + VoiceHub
    └── wwwroot/
        ├── css/larrat.css    # Design system completo
        └── js/               # WebRTC + Audio + Crypto
```

## Configuração para Produção

1. **TURN Server** — Para WebRTC funcionar em redes NAT restritivas, configure um servidor TURN (ex: [coturn](https://github.com/coturn/coturn)):
   ```json
   // Em appsettings.json:
   "WebRTC": {
     "TurnServer": "turn:seu-servidor.com:3478",
     "TurnUsername": "user",
     "TurnCredential": "password"
   }
   ```

2. **Redis** — Para múltiplos servidores, substitua o `Dictionary<>` em memória dos Hubs por backplane Redis:
   ```bash
   dotnet add package Microsoft.AspNetCore.SignalR.StackExchangeRedis
   ```

3. **Storage** — Para produção, mova os uploads para Azure Blob Storage ou S3.

4. **SSL** — Obrigatório para WebRTC e câmera/microfone.

## Convite para Grupos

Cada grupo tem um código de convite único (8 caracteres alfanuméricos).
Compartilhe o link: `https://larrat.app/invite/ABCD1234`

## Licença

Propriedade de Larrat. Todos os direitos reservados.
