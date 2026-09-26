using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Larrat.Core.Entities;

namespace Larrat.Core.Data;

public class LarratDbContext : IdentityDbContext<ApplicationUser>
{
    public LarratDbContext(DbContextOptions<LarratDbContext> options) : base(options) { }

    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<VoiceRoom> VoiceRooms => Set<VoiceRoom>();
    public DbSet<VoiceRoomParticipant> VoiceRoomParticipants => Set<VoiceRoomParticipant>();
    public DbSet<VoiceSubRoom> VoiceSubRooms => Set<VoiceSubRoom>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
    public DbSet<DirectConversation> DirectConversations => Set<DirectConversation>();
    public DbSet<DirectConversationParticipant> DirectConversationParticipants => Set<DirectConversationParticipant>();
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ApplicationUser
        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            e.Property(u => u.StatusMessage).HasMaxLength(200);
        });

        // Group
        builder.Entity<Group>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.Name).HasMaxLength(100).IsRequired();
            e.Property(g => g.Description).HasMaxLength(500);
            e.HasOne(g => g.Owner).WithMany().HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(g => g.Members).WithOne(m => m.Group).HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(g => g.Channels).WithOne(c => c.Group).HasForeignKey(c => c.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(g => g.VoiceRooms).WithOne(v => v.Group).HasForeignKey(v => v.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(g => g.InviteCode).IsUnique();
        });

        // GroupMember
        builder.Entity<GroupMember>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();
            e.HasOne(m => m.User).WithMany(u => u.GroupMemberships).HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // Channel
        builder.Entity<Channel>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(100).IsRequired();
            e.HasMany(c => c.Messages).WithOne(m => m.Channel).HasForeignKey(m => m.ChannelId).OnDelete(DeleteBehavior.Cascade);
        });

        // VoiceRoom
        builder.Entity<VoiceRoom>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Name).HasMaxLength(100).IsRequired();
            e.HasMany(v => v.Participants).WithOne(p => p.VoiceRoom).HasForeignKey(p => p.VoiceRoomId).OnDelete(DeleteBehavior.Cascade);
        });

        // VoiceRoomParticipant - índice único por room + user
        builder.Entity<VoiceRoomParticipant>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.VoiceRoomId, p.UserId }).IsUnique();
        });

        // VoiceSubRoom
        builder.Entity<VoiceSubRoom>(e =>
        {
            e.HasKey(v => v.Id);
            e.HasOne(v => v.ParentVoiceRoom).WithMany().HasForeignKey(v => v.ParentVoiceRoomId).OnDelete(DeleteBehavior.Cascade);
        });

        // Message
        builder.Entity<Message>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasOne(m => m.Sender).WithMany(u => u.SentMessages).HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.ReplyToMessage).WithMany().HasForeignKey(m => m.ReplyToMessageId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.Attachment).WithMany().HasForeignKey(m => m.AttachmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.Channel).WithMany(c => c.Messages).HasForeignKey(m => m.ChannelId).OnDelete(DeleteBehavior.Cascade).IsRequired(false);
            e.HasOne(m => m.DirectConversation).WithMany(dc => dc.Messages).HasForeignKey(m => m.DirectConversationId).OnDelete(DeleteBehavior.Cascade).IsRequired(false);
            e.HasMany(m => m.Reactions).WithOne(r => r.Message).HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
        });

        // MessageReaction - índice único por mensagem + usuário + emoji
        builder.Entity<MessageReaction>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.MessageId, r.UserId, r.Emoji }).IsUnique();
        });

        // DirectConversation
        builder.Entity<DirectConversation>(e =>
        {
            e.HasKey(dc => dc.Id);
            e.HasMany(dc => dc.Participants).WithOne(p => p.DirectConversation).HasForeignKey(p => p.DirectConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        // DirectConversationParticipant
        builder.Entity<DirectConversationParticipant>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.DirectConversationId, p.UserId }).IsUnique();
            e.HasOne(p => p.User).WithMany(u => u.DirectConversations).HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // FileAttachment
        builder.Entity<FileAttachment>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.OriginalFileName).HasMaxLength(500).IsRequired();
            e.Property(f => f.ContentType).HasMaxLength(200).IsRequired();
            e.HasOne(f => f.Uploader).WithMany().HasForeignKey(f => f.UploaderUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
