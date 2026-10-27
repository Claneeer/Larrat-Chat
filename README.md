# Larrat Chat

Larrat Chat é uma plataforma de comunicação distribuída em tempo real, projetada para troca de mensagens criptografadas, canais de voz WebRTC, compartilhamento de arquivos e organização por grupos e canais. A solução combina ASP.NET Core, Blazor, SignalR e Entity Framework Core para oferecer uma experiência de chat moderna, segura e colaborativa.

## Visão geral

A plataforma foi concebida para manter a comunicação em tempo real com alta disponibilidade e baixa fricção operacional. Os usuários podem trocar mensagens em canais dedicados, participar de grupos, consumir arquivos e criar salas de voz para reuniões rápidas ou conversas em equipe.

Entre os principais diferenciais estão:

- Mensagens de texto com criptografia aplicada antes do armazenamento
- Canais de texto e salas de voz organizados por grupo
- Arquivos enviados por usuários com controle de tamanho e persistência
- Autenticação com Identity + JWT
- WebRTC para voz e vídeo em tempo real
- Estrutura modular com separação clara entre domínio e apresentação

## Arquitetura e tecnologias

### Back-end e Core

O projeto principal do domínio está em `src/Larrat.Core`, onde ficam:

- Entidades do sistema: usuários, grupos, canais, mensagens, arquivos e salas de voz
- `LarratDbContext` e migrações do Entity Framework Core
- Serviços de criptografia (`EncryptionService`)
- Serviços de gerenciamento de autenticação e JWT (`TokenService`)
- Serviços de grupo, mensagem e arquivos (`GroupService`, `MessageService`, `FileService`)

Esses componentes são responsáveis por persistência, regras de negócio e proteção de dados sensíveis.

### Web e real-time

A camada web fica em `src/Larrat.Web` e inclui:

- ASP.NET Core com aplicação Blazor
- Componentes Razor para autenticação e chat
- SignalR para comunicação em tempo real (`ChatHub` e `VoiceHub`)
- WebRTC para voz/vídeo (`wwwroot/js/webrtc.js` e `wwwroot/js/audio-recorder.js`)
- Controle de upload e APIs REST para arquivos

## Estrutura da solução

```text
Larrat-Chat/
├── README.md
├── Larrat.slnx
├── src/
│   ├── Larrat.Core/
│   │   ├── Data/
│   │   ├── Entities/
│   │   └── Services/
│   └── Larrat.Web/
│       ├── Components/
│       ├── Controllers/
│       ├── Hubs/
│       ├── wwwroot/
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── appsettings.example.json
└── ...
```

## Pré-requisitos e ambiente

Antes de executar o projeto, confirme que o ambiente atende aos requisitos abaixo:

- .NET 8 SDK
- PostgreSQL disponível localmente ou via container
- Git para controle de versão do repositório

### Configuração do banco

O banco padrão do projeto é:

- `larrat-Chat`

A connection string esperada é semelhante a:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=larrat-Chat;Username=postgres;Password=postgres"
  },
  "JwtSettings": {
    "SecretKey": "LarratDevSecretKey_32_chars_1234567890",
    "Issuer": "LarratChat",
    "Audience": "LarratChat"
  }
}
```

> Em desenvolvimento, os valores acima já estão preparados para funcionar. Em produção, substitua a secret key por um valor forte e exclusivo.

## Como executar

No diretório raiz do repositório, execute:

```bash
dotnet restore
dotnet build Larrat.slnx
```

Para rodar a aplicação:

```bash
dotnet run --project src/Larrat.Web
```

Ao iniciar, a aplicação realiza automaticamente:

1. Criação do banco `larrat-Chat` quando necessário
2. Aplicação das migrações do Entity Framework Core
3. Seed de usuários de teste e grupo padrão

## Contas de teste pré-configuradas

A inicialização automática do banco já cria duas contas prontas para uso:

| Conta | E-mail | Senha |
| :--- | :--- | :--- |
| **Usuário Teste 1** | `Larrat@example.com` | `senha123` |
| **Usuário Teste 2** | `example@larrat.com` | `senha321` |

## Observações

O projeto foi preparado para que a autenticação e o chat funcionem diretamente em ambiente local sem etapas manuais adicionais, com migrações e dados iniciais sendo executados no startup da aplicação.
