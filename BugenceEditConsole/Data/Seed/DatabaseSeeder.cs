
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BugenceEditConsole.Data.Seed;

public static class DatabaseSeeder
{
    private static readonly HashSet<string> LegacyMediaPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "/images/bugence-logo.png",
        "/images/bugence-logo.svg"
    };

    private static readonly HashSet<string> LegacyMediaAltPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stage lighting and audience silhouettes",
        "Community collaboration collage",
        "Bugence mark"
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var scopedServices = scope.ServiceProvider;

        var logger = scopedServices.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSeeder");
        var db = scopedServices.GetRequiredService<ApplicationDbContext>();
        var userManager = scopedServices.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.MigrateAsync();

        await SeedDefaultUserAsync(db, userManager, logger);
        await RepairCompanyIsolationAsync(db, userManager, logger);
        await SeedSiteContentAsync(db, logger);
    }

    private static async Task SeedDefaultUserAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ILogger logger)
    {
        const string email = "admin@bugence.com";
        const string password = "Bugence!2025";
        const string userName = "admin";

        var existingUser = await userManager.FindByNameAsync(userName) ??
                           await userManager.FindByEmailAsync(email);

        if (existingUser is null)
        {
            var user = new ApplicationUser
            {
                UserName = userName,
                NormalizedUserName = userName.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                DisplayName = "Admin",
                FirstName = "Admin",
                LastName = "User"
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                var message = string.Join("; ", createResult.Errors.Select(e => e.Description));
                logger.LogError("Failed to seed default user: {Message}", message);
                return;
            }

            logger.LogInformation("Seeded default operator account for {Email}", email);
            await EnsureSeedCompanyAsync(db, userManager, user);
            return;
        }

        // Update existing admin account to the new email/password
        await EnsureUserCompanyLinkValidAsync(db, existingUser);
        existingUser = await userManager.FindByIdAsync(existingUser.Id) ?? existingUser;

        var needsEmailUpdate = !string.Equals(existingUser.Email, email, StringComparison.OrdinalIgnoreCase);
        if (needsEmailUpdate)
        {
            existingUser.Email = email;
            existingUser.NormalizedEmail = email.ToUpperInvariant();
            existingUser.EmailConfirmed = true;
            await userManager.UpdateAsync(existingUser);
        }

        var removePw = await userManager.RemovePasswordAsync(existingUser);
        if (!removePw.Succeeded)
        {
            var msg = string.Join("; ", removePw.Errors.Select(e => e.Description));
            logger.LogWarning("Could not remove existing admin password: {Message}", msg);
        }
        var addPw = await userManager.AddPasswordAsync(existingUser, password);
        if (!addPw.Succeeded)
        {
            var msg = string.Join("; ", addPw.Errors.Select(e => e.Description));
            logger.LogError("Could not reset admin password: {Message}", msg);
        }

        logger.LogInformation("Updated admin account to {Email}", email);
        await EnsureSeedCompanyAsync(db, userManager, existingUser);
    }

    private static async Task EnsureSeedCompanyAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        await EnsureUserCompanyLinkValidAsync(db, user);

        if (user.CompanyId.HasValue)
        {
            return;
        }

        var company = new CompanyProfile
        {
            Name = "Bugence",
            CreatedByUserId = user.Id,
            ExpectedUserCount = 5
        };
        db.CompanyProfiles.Add(company);
        await db.SaveChangesAsync();

        user.CompanyId = company.Id;
        user.IsCompanyAdmin = true;
        await userManager.UpdateAsync(user);
    }

    private static async Task EnsureUserCompanyLinkValidAsync(ApplicationDbContext db, ApplicationUser user)
    {
        if (!user.CompanyId.HasValue)
        {
            return;
        }

        var exists = await db.CompanyProfiles.AnyAsync(c => c.Id == user.CompanyId.Value);
        if (exists)
        {
            return;
        }

        // Heal legacy broken foreign key link before Identity updates.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE AspNetUsers SET CompanyId = NULL, IsCompanyAdmin = 0 WHERE Id = {user.Id}");
        user.CompanyId = null;
        user.IsCompanyAdmin = false;
    }

    private static async Task RepairCompanyIsolationAsync(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ILogger logger)
    {
        const string adminEmail = "admin@bugence.com";
        const string hadiEmail = "hadihafeez100@gmail.com";

        var admin = await userManager.FindByEmailAsync(adminEmail);
        var hadi = await userManager.FindByEmailAsync(hadiEmail);
        if (admin == null && hadi == null)
        {
            return;
        }

        if (admin != null)
        {
            await EnsureUserCompanyLinkValidAsync(db, admin);
            admin = await userManager.FindByIdAsync(admin.Id) ?? admin;
        }

        if (hadi != null)
        {
            await EnsureUserCompanyLinkValidAsync(db, hadi);
            hadi = await userManager.FindByIdAsync(hadi.Id) ?? hadi;
        }

        var usersUpdated = 0;
        var projectsRestamped = 0;
        var workflowsRestamped = 0;
        var teamLinksRemoved = 0;

        if (admin != null &&
            hadi != null &&
            admin.CompanyId.HasValue &&
            admin.CompanyId == hadi.CompanyId)
        {
            var hadiCompany = new CompanyProfile
            {
                Name = "Hadi Hafeez",
                CreatedByUserId = hadi.Id,
                ExpectedUserCount = 5
            };
            db.CompanyProfiles.Add(hadiCompany);
            await db.SaveChangesAsync();

            hadi.CompanyId = hadiCompany.Id;
            hadi.IsCompanyAdmin = true;
            var update = await userManager.UpdateAsync(hadi);
            if (update.Succeeded)
            {
                usersUpdated++;
            }
            else
            {
                logger.LogWarning(
                    "Company isolation repair could not split {Email}: {Errors}",
                    hadiEmail,
                    string.Join("; ", update.Errors.Select(e => e.Description)));
            }
        }

        if (hadi != null && !hadi.CompanyId.HasValue)
        {
            var hadiCompany = new CompanyProfile
            {
                Name = "Hadi Hafeez",
                CreatedByUserId = hadi.Id,
                ExpectedUserCount = 5
            };
            db.CompanyProfiles.Add(hadiCompany);
            await db.SaveChangesAsync();

            hadi.CompanyId = hadiCompany.Id;
            hadi.IsCompanyAdmin = true;
            var update = await userManager.UpdateAsync(hadi);
            if (update.Succeeded)
            {
                usersUpdated++;
            }
            else
            {
                logger.LogWarning(
                    "Company isolation repair could not assign company to {Email}: {Errors}",
                    hadiEmail,
                    string.Join("; ", update.Errors.Select(e => e.Description)));
            }
        }

        var userCompanyLookup = await db.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.CompanyId })
            .ToDictionaryAsync(u => u.Id, u => u.CompanyId);

        var projects = await db.UploadedProjects.ToListAsync();
        foreach (var project in projects)
        {
            if (string.IsNullOrWhiteSpace(project.UserId) ||
                !userCompanyLookup.TryGetValue(project.UserId, out var ownerCompanyId))
            {
                continue;
            }

            if (project.CompanyId != ownerCompanyId)
            {
                project.CompanyId = ownerCompanyId;
                projectsRestamped++;
            }
        }
        if (projectsRestamped > 0)
        {
            await db.SaveChangesAsync();
        }

        var workflows = await db.Workflows.ToListAsync();
        foreach (var workflow in workflows)
        {
            if (string.IsNullOrWhiteSpace(workflow.OwnerUserId) ||
                !userCompanyLookup.TryGetValue(workflow.OwnerUserId, out var ownerCompanyId))
            {
                continue;
            }

            if (workflow.CompanyId != ownerCompanyId)
            {
                workflow.CompanyId = ownerCompanyId;
                workflowsRestamped++;
            }
        }
        if (workflowsRestamped > 0)
        {
            await db.SaveChangesAsync();
        }

        var staleTeamMembers = await db.TeamMembers
            .Where(m => m.UserId != null)
            .ToListAsync();
        foreach (var teamMember in staleTeamMembers)
        {
            if (string.IsNullOrWhiteSpace(teamMember.UserId) ||
                !userCompanyLookup.TryGetValue(teamMember.OwnerUserId, out var ownerCompanyId) ||
                !userCompanyLookup.TryGetValue(teamMember.UserId, out var memberCompanyId) ||
                ownerCompanyId != memberCompanyId)
            {
                db.TeamMembers.Remove(teamMember);
                teamLinksRemoved++;
            }
        }
        if (teamLinksRemoved > 0)
        {
            await db.SaveChangesAsync();
        }

        if (usersUpdated > 0 || projectsRestamped > 0 || workflowsRestamped > 0 || teamLinksRemoved > 0)
        {
            logger.LogInformation(
                "Company isolation repair applied. UsersUpdated={UsersUpdated}, ProjectsRestamped={ProjectsRestamped}, WorkflowsRestamped={WorkflowsRestamped}, TeamLinksRemoved={TeamLinksRemoved}",
                usersUpdated,
                projectsRestamped,
                workflowsRestamped,
                teamLinksRemoved);
        }
    }

    private static async Task SeedSiteContentAsync(ApplicationDbContext db, ILogger logger)
    {
        var now = DateTime.UtcNow;

        var pageSeeds = new[]
        {
            new PageSeed(
                Name: "Index",
                Slug: "index",
                Description: "Bugence Visual Editor landing deck with a neon control-room experience.",
                HeroImagePath: "/images/bugence-logo.svg",
                Sections: new[]
                {
                    new SectionSeed(
                        SectionKey: "hero_title",
                        Title: "Mission Headline",
                        ContentType: SectionContentType.Text,
                        ContentValue: "Bugence Visual Editor, orchestrated in real time.",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 0),
                    new SectionSeed(
                        SectionKey: "hero_story",
                        Title: "Hero Story",
                        ContentType: SectionContentType.RichText,
                        ContentValue: "Upload any static site, auto-prepare, and edit visually with zero code. <strong>Mission control</strong> is yours from the first click.",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 1),
                    new SectionSeed(
                        SectionKey: "hero_metrics",
                        Title: "Impact Metrics",
                        ContentType: SectionContentType.RichText,
                        ContentValue: "<ul><li>1-step upload to live preview</li><li>Full-site text/link/image editing</li><li>Publish + rollback built in</li></ul>",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 2),
                    new SectionSeed(
                        SectionKey: "hero_image",
                        Title: "Hero Marquee",
                        ContentType: SectionContentType.Image,
                        ContentValue: null,
                        MediaPath: "/images/bugence-logo.svg",
                        MediaAltText: "Bugence luminous mark",
                        DisplayOrder: 3)
                }),
            new PageSeed(
                Name: "Product Canvas",
                Slug: "meet-pete-d",
                Description: "Showcase the Bugence canvas with product storytelling and visual editor proof points.",
                HeroImagePath: "/images/bugence-logo.svg",
                Sections: new[]
                {
                    new SectionSeed(
                        SectionKey: "hero_title",
                        Title: "Opening Statement",
                        ContentType: SectionContentType.Text,
                        ContentValue: "Meet the canvas that turns any static site into a living experience.",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 0),
                    new SectionSeed(
                        SectionKey: "hero_bio",
                        Title: "Spotlight Narrative",
                        ContentType: SectionContentType.RichText,
                        ContentValue: "Bugence injects editor hooks, normalizes assets, and keeps your team in flow with neon-grade feedback and draft-to-publish guardrails.",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 1),
                    new SectionSeed(
                        SectionKey: "hero_quote",
                        Title: "Signature Quote",
                        ContentType: SectionContentType.RichText,
                        ContentValue: "\"Upload. Prepare. Edit. Ship. Your entire site becomes editable without touching code.\"",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 2),
                    new SectionSeed(
                        SectionKey: "hero_portrait",
                        Title: "Feature Visual",
                        ContentType: SectionContentType.Image,
                        ContentValue: null,
                        MediaPath: "/images/profile-placeholder.jpg",
                        MediaAltText: "Bugence interface mock on a neon gradient",
                        DisplayOrder: 3)
                }),
            new PageSeed(
                Name: "Engage Bugence",
                Slug: "book-pete-d",
                Description: "Engagement console for demos, onboarding, and customer activation.",
                HeroImagePath: "/images/bugence-logo.svg",
                Sections: new[]
                {
                    new SectionSeed(
                        SectionKey: "hero_title",
                        Title: "Booking Headline",
                        ContentType: SectionContentType.Text,
                        ContentValue: "Book a Bugence demo or onboarding session.",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 0),
                    new SectionSeed(
                        SectionKey: "hero_pitch",
                        Title: "Booking Narrative",
                        ContentType: SectionContentType.RichText,
                        ContentValue: "From upload to live preview to visual edit, we walk your team through the exact flow that keeps clients engaged and buying.",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 1),
                    new SectionSeed(
                        SectionKey: "booking_cta",
                        Title: "Primary Call-to-Action",
                        ContentType: SectionContentType.Text,
                        ContentValue: "Schedule a live walkthrough",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 2),
                    new SectionSeed(
                        SectionKey: "booking_visual",
                        Title: "Booking Visual",
                        ContentType: SectionContentType.Image,
                        ContentValue: null,
                        MediaPath: "/Img/author3.svg",
                        MediaAltText: "Stylized device screens with neon gradients",
                        DisplayOrder: 3)
                }),
            new PageSeed(
                Name: "Join Operators",
                Slug: "join-community",
                Description: "Operator portal for updates, releases, and community access.",
                HeroImagePath: "/images/bugence-logo.svg",
                Sections: new[]
                {
                    new SectionSeed(
                        SectionKey: "hero_title",
                        Title: "Community Rally Cry",
                        ContentType: SectionContentType.Text,
                        ContentValue: "Join the Bugence operator signal.",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 0),
                    new SectionSeed(
                        SectionKey: "hero_body",
                        Title: "Community Narrative",
                        ContentType: SectionContentType.RichText,
                        ContentValue: "Receive drops on feature releases, neon UI packs, and guides for turning static sites into visual-editable canvases.",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 1),
                    new SectionSeed(
                        SectionKey: "signup_cta",
                        Title: "Signup CTA",
                        ContentType: SectionContentType.Text,
                        ContentValue: "Activate my operator access",
                        MediaPath: null,
                        MediaAltText: null,
                        DisplayOrder: 2),
                    new SectionSeed(
                        SectionKey: "community_visual",
                        Title: "Community Visual",
                        ContentType: SectionContentType.Image,
                        ContentValue: null,
                        MediaPath: "/Img/author4.svg",
                        MediaAltText: "Neon gradient community visual",
                        DisplayOrder: 3)
                })
        };

        foreach (var seed in pageSeeds)
        {
            var page = await db.SitePages
                .Include(p => p.Sections)
                .FirstOrDefaultAsync(p => p.Slug == seed.Slug);

            if (page is null)
            {
                page = new SitePage
                {
                    Name = seed.Name,
                    Slug = seed.Slug,
                    Description = seed.Description,
                    HeroImagePath = seed.HeroImagePath,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

                foreach (var sectionSeed in seed.Sections)
                {
                    page.Sections.Add(CreateSection(page, sectionSeed, now));
                }

                db.SitePages.Add(page);
            }
            else
            {
                var metadataChanged = false;

                if (!string.Equals(page.Name, seed.Name, StringComparison.Ordinal))
                {
                    page.Name = seed.Name;
                    metadataChanged = true;
                }

                if (!string.Equals(page.Description, seed.Description, StringComparison.Ordinal))
                {
                    page.Description = seed.Description;
                    metadataChanged = true;
                }

                if (!string.Equals(page.HeroImagePath, seed.HeroImagePath, StringComparison.Ordinal))
                {
                    page.HeroImagePath = seed.HeroImagePath;
                    metadataChanged = true;
                }

                if (metadataChanged)
                {
                    page.UpdatedAtUtc = now;
                }
            }

            var sectionLookup = page.Sections.ToDictionary(s => s.SectionKey, StringComparer.OrdinalIgnoreCase);

            foreach (var sectionSeed in seed.Sections)
            {
                if (!sectionLookup.TryGetValue(sectionSeed.SectionKey, out var section))
                {
                    section = CreateSection(page, sectionSeed, now);
                    page.Sections.Add(section);
                    continue;
                }

                var sectionChanged = false;

                if (!string.Equals(section.Title, sectionSeed.Title, StringComparison.Ordinal))
                {
                    section.Title = sectionSeed.Title;
                    sectionChanged = true;
                }

                if (section.ContentType != sectionSeed.ContentType)
                {
                    section.ContentType = sectionSeed.ContentType;
                    sectionChanged = true;
                }

                if (section.DisplayOrder != sectionSeed.DisplayOrder)
                {
                    section.DisplayOrder = sectionSeed.DisplayOrder;
                    sectionChanged = true;
                }

                if (section.IsLocked != sectionSeed.IsLocked)
                {
                    section.IsLocked = sectionSeed.IsLocked;
                    sectionChanged = true;
                }

                if (string.IsNullOrWhiteSpace(section.ContentValue) && !string.IsNullOrWhiteSpace(sectionSeed.ContentValue))
                {
                    section.ContentValue = sectionSeed.ContentValue;
                    sectionChanged = true;
                }

                if (string.IsNullOrWhiteSpace(section.MediaPath) && !string.IsNullOrWhiteSpace(sectionSeed.MediaPath))
                {
                    section.MediaPath = sectionSeed.MediaPath;
                    sectionChanged = true;
                }
                else if (!string.IsNullOrWhiteSpace(section.MediaPath) &&
                         !string.IsNullOrWhiteSpace(sectionSeed.MediaPath) &&
                         LegacyMediaPlaceholders.Contains(section.MediaPath))
                {
                    section.MediaPath = sectionSeed.MediaPath;
                    sectionChanged = true;
                }

                if (string.IsNullOrWhiteSpace(section.MediaAltText) && !string.IsNullOrWhiteSpace(sectionSeed.MediaAltText))
                {
                    section.MediaAltText = sectionSeed.MediaAltText;
                    sectionChanged = true;
                }
                else if (!string.IsNullOrWhiteSpace(section.MediaAltText) &&
                         !string.IsNullOrWhiteSpace(sectionSeed.MediaAltText) &&
                         LegacyMediaAltPlaceholders.Contains(section.MediaAltText))
                {
                    section.MediaAltText = sectionSeed.MediaAltText;
                    sectionChanged = true;
                }

                if (sectionChanged)
                {
                    section.UpdatedAtUtc = now;
                }
            }

            // Custom sections created through the editor remain untouched.
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded site pages and sections for mission control experience.");
    }

    private static PageSection CreateSection(SitePage page, SectionSeed sectionSeed, DateTime timestamp) =>
        new()
        {
            SitePage = page,
            SectionKey = sectionSeed.SectionKey,
            Title = sectionSeed.Title,
            ContentType = sectionSeed.ContentType,
            ContentValue = sectionSeed.ContentValue,
            MediaPath = sectionSeed.MediaPath,
            MediaAltText = sectionSeed.MediaAltText,
            DisplayOrder = sectionSeed.DisplayOrder,
            IsLocked = sectionSeed.IsLocked,
            UpdatedAtUtc = timestamp
        };

    private sealed record PageSeed(
        string Name,
        string Slug,
        string Description,
        string? HeroImagePath,
        IReadOnlyList<SectionSeed> Sections);

    private sealed record SectionSeed(
        string SectionKey,
        string Title,
        SectionContentType ContentType,
        string? ContentValue,
        string? MediaPath,
        string? MediaAltText,
        int DisplayOrder,
        bool IsLocked = false);
}
