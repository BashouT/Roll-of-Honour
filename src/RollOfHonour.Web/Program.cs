using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using RollOfHonour.Core;
using RollOfHonour.Core.Authorization;
using RollOfHonour.Core.Search;
using RollOfHonour.Core.Shared;
using RollOfHonour.Data.Context;
using RollOfHonour.Data.Models.DB;
using RollOfHonour.Data.Repositories;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddAzureAppConfiguration(builder.Configuration.GetConnectionString("AzureAppConfiguration"));
builder.Services.AddDbContext<RollOfHonourContext>(options =>
    options.UseSqlServer(System.Environment.GetEnvironmentVariable("ConnectionString"),
        x => x.UseNetTopologySuite()));

builder.Services.Configure<Storage>(builder.Configuration.GetSection(nameof(AppSettings.Storage)));
builder.Services.Configure<Whitelists>(builder.Configuration.GetSection(nameof(AppSettings.Whitelists)));
builder.Services.Configure<APIBasicAuth>(builder.Configuration.GetSection(nameof(AppSettings.APIConnectorsAuth)));
builder.Services.AddTransient<IPersonRepository, PersonRepository>();
builder.Services.AddTransient<IMemorialRepository, MemorialRepository>();
builder.Services.AddTransient<IUserRepository, UserRepository>();
builder.Services.AddTransient<IRoleRepository, RoleRepository>();
builder.Services.AddTransient<ISuperSearchService, SuperSearchService>();

builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAdB2C");

//builder.Services.AddAuthorization(options =>
//{
//    options.FallbackPolicy = options.DefaultPolicy;
//});

builder.Services.AddRazorPages()
    .AddMicrosoftIdentityUI();

builder.Services.AddSignalR().AddAzureSignalR(options =>
{
    options.ServerStickyMode = Microsoft.Azure.SignalR.ServerStickyMode.Required;
});

builder.Services.AddServerSideBlazor();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicyNames.EditPerson, policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(claim =>
                claim.Type is AuthorizationClaims.AdministratorPersonEdit
                    or AuthorizationClaims.ModeratorPersonEdit)));

    options.AddPolicy(AuthorizationPolicyNames.EditMemorial, policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(claim =>
                claim.Type is AuthorizationClaims.AdministratorMemorialEdit
                    or AuthorizationClaims.ModeratorMemorialEdit)));

    options.AddPolicy(AuthorizationPolicyNames.EditUser, policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(claim =>
                claim.Type is AuthorizationClaims.AdministratorUserEdit)));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var context = services.GetRequiredService<RollOfHonourContext>();

    context.Database.Migrate();

    // seed db roles/claims
    var appRoles = new List<Role>
    {
        new()
        {
            Name = ApplicationRoles.User,
            IsActive = true
        },
        new()
        {
            Name = ApplicationRoles.Administrator,
            IsActive = true
        },
        new()
        {
            Name = ApplicationRoles.Moderator,
            IsActive = true,
        },
        new()
        {
            Name = ApplicationRoles.StaffAdmin,
            IsActive = true
        }
    };
    appRoles.ForEach(item => context.Roles.AddIfNotExists(item, role => role.Name == item.Name));
    context.SaveChanges();

    var appAdministratorRole = context.Roles.Single(_ => _.Name == ApplicationRoles.Administrator);
    var appModeratorRole = context.Roles.Single(_ => _.Name == ApplicationRoles.Moderator);
    var appStaffAdminRole = context.Roles.Single(_ => _.Name == ApplicationRoles.StaffAdmin);
    var appClaims = new List<Claim>
    {
        // Admin
        new() { Name = $"{AuthorizationClaims.AdministratorPersonEdit}", Role = appAdministratorRole },
        new() { Name = $"{AuthorizationClaims.AdministratorMemorialEdit}", Role = appAdministratorRole },
        // Moderator
        new() { Name = $"{AuthorizationClaims.ModeratorPersonEdit}", Role = appModeratorRole },
        new() { Name = $"{AuthorizationClaims.ModeratorMemorialEdit}", Role = appModeratorRole },
        // Staff admin - Basically Admin but ability to edit users and their roles
        new() { Name = $"{AuthorizationClaims.AdministratorUserEdit}", Role = appStaffAdminRole },
        new() { Name = $"{AuthorizationClaims.AdministratorPersonEdit}", Role = appStaffAdminRole },
        new() { Name = $"{AuthorizationClaims.AdministratorMemorialEdit}", Role = appStaffAdminRole }
    };
    appClaims.ForEach(item => context.Claims.AddIfNotExists(item, claim => claim.Name == item.Name && claim.Role.Id == item.Role.Id));
    context.SaveChanges();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
        name: "MemorialRemoveCitizen",
        pattern: "{controller=Memorial}/{action=Remove}/{memorialId?}/{citizenId?}");
});

app.MapRazorPages();
app.MapBlazorHub();
app.MapControllers();
app.MapFallbackToPage("/_Host");

app.Run();

