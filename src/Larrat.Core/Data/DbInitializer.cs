using Larrat.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Larrat.Core.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var dbContext = services.GetRequiredService<LarratDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        await EnsureDatabaseExistsAsync(dbContext);
        await dbContext.Database.MigrateAsync();

        foreach (var roleName in new[] { "User", "Admin" })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        var seedUsers = new[]
        {
            new
            {
                Email = "Larrat@example.com",
                UserName = "Larrat@example.com",
                DisplayName = "Larrat",
                Password = "senha123",
                Role = "User"
            },
            new
            {
                Email = "example@larrat.com",
                UserName = "example@larrat.com",
                DisplayName = "Example",
                Password = "senha321",
                Role = "User"
            }
        };

        foreach (var seedUser in seedUsers)
        {
            var existingUser = await userManager.FindByEmailAsync(seedUser.Email);

            if (existingUser is null)
            {
                var user = new ApplicationUser
                {
                    UserName = seedUser.UserName,
                    Email = seedUser.Email,
                    DisplayName = seedUser.DisplayName,
                    EmailConfirmed = true,
                    Status = UserStatus.Online,
                    CreatedAt = DateTime.UtcNow
                };

                var result = await userManager.CreateAsync(user, seedUser.Password);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Falha ao criar usuário {seedUser.Email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }

                if (!string.IsNullOrWhiteSpace(seedUser.Role))
                {
                    await userManager.AddToRoleAsync(user, seedUser.Role);
                }

                continue;
            }

            existingUser.DisplayName ??= seedUser.DisplayName;
            existingUser.EmailConfirmed = true;
            existingUser.Status = UserStatus.Online;

            var updateResult = await userManager.UpdateAsync(existingUser);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Falha ao atualizar usuário {seedUser.Email}: {string.Join(", ", updateResult.Errors.Select(e => e.Description))}");
            }

            var hasPassword = await userManager.CheckPasswordAsync(existingUser, seedUser.Password);
            if (!hasPassword)
            {
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(existingUser);
                var passwordResult = await userManager.ResetPasswordAsync(existingUser, resetToken, seedUser.Password);
                if (!passwordResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Falha ao redefinir senha do usuário {seedUser.Email}: {string.Join(", ", passwordResult.Errors.Select(e => e.Description))}");
                }
            }
        }

        var defaultGroupName = "Larrat Geral";
        var groupExists = await dbContext.Groups.AnyAsync(g => g.Name == defaultGroupName);
        if (!groupExists)
        {
            var owner = await userManager.FindByEmailAsync("Larrat@example.com");
            var member = await userManager.FindByEmailAsync("example@larrat.com");

            if (owner is null || member is null)
            {
                throw new InvalidOperationException("Não foi possível localizar os usuários de seed para criar o grupo padrão.");
            }

            var defaultGroup = new Group
            {
                Name = defaultGroupName,
                Description = "Grupo principal de demonstração do Larrat Chat.",
                OwnerId = owner.Id,
                InviteCode = GenerateInviteCode(),
                Members =
                {
                    new GroupMember { UserId = owner.Id, Role = GroupRole.Owner },
                    new GroupMember { UserId = member.Id, Role = GroupRole.Member }
                },
                Channels =
                {
                    new Channel { Name = "geral", IsDefault = true, Position = 0 },
                    new Channel { Name = "dicas", Position = 1 }
                },
                VoiceRooms =
                {
                    new VoiceRoom { Name = "Voz Geral", Position = 0 }
                }
            };

            dbContext.Groups.Add(defaultGroup);
            await dbContext.SaveChangesAsync();
        }
    }

    private static async Task EnsureDatabaseExistsAsync(LarratDbContext dbContext)
    {
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database;
        if (string.IsNullOrWhiteSpace(databaseName) || databaseName.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        builder.Database = "postgres";
        await using var adminConnection = new Npgsql.NpgsqlConnection(builder.ConnectionString);
        await adminConnection.OpenAsync();

        await using var command = adminConnection.CreateCommand();
        command.CommandText = @"
            SELECT 1
            FROM pg_database
            WHERE datname = @databaseName;";
        command.Parameters.AddWithValue("@databaseName", databaseName);

        var exists = await command.ExecuteScalarAsync();
        if (exists is null)
        {
            await using var createCommand = adminConnection.CreateCommand();
            createCommand.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            await createCommand.ExecuteNonQueryAsync();
        }
    }

    private static string GenerateInviteCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
    }
}
