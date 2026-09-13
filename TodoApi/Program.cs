using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Components.Web;
using Scalar.AspNetCore;
using Microsoft.OpenApi;

using TodoApi.Dtos;
using TodoApi. Models;
using TodoApi.Data;
using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\""
        };

        return Task.CompletedTask;
    });
});

builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

var jwtKey = builder.Configuration["Jwt:Key"]!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var todoGroup = app.MapGroup("/api/todos").WithTags("Todos");

#region In-Memory Endpoints

// var todos = new List<TodoGetDto>
// {
//     new(1, "Learn Minimal API", false),
//     new(2, "Learn Vue", false),
//     new(3, "Build a web API", false)
// };

// todoGroup.MapGet("/", () => Results.Ok(todos));

// todoGroup.MapGet("/{id}", (int id) =>
// {
//     var todo = todos.FirstOrDefault(t => t.Id == id);

//     return todo is not null ? Results.Ok(todo) : Results.NotFound();

// });

// todoGroup.MapPost("/", (TodoPostDto dto) =>
// {
//     var nextId = todos.Count == 0 ? 1 : todos.Max(t => t.Id) + 1;

//     var todo = new TodoGetDto(nextId, dto.Title, false);
//     todos.Add(todo);

//     return Results.Created($"/api/todos/{todo.Id}", todo);
// });

// todoGroup.MapPut("/{id}", (int id, TodoPostDto dto) =>
// {
//     try
//     {
//         var index = todos.FindIndex(t => t.Id == id);
//         if (index == -1) return Results.NotFound();

//         todos[index] = todos[index] with
//         {
//             Title = dto.Title,
//             IsCompleted = dto.IsCompleted
//         };

//         return Results.Ok(todos[index]);
//     }
//     catch (Exception ex)
//     {
//         return Results.Problem(ex.Message);
//     }

// });

// todoGroup.MapDelete("/{id}", (int id) =>
// {
//     try
//     {
//         var todo = todos.FirstOrDefault(t => t.Id == id);
//         if (todo is null) return Results.NotFound();

//         todos.Remove(todo);
//         return Results.NoContent();
//     }
//     catch (ArgumentNullException ex)
//     {
//         return Results.Problem("Parameter is null.");
//     }
//     catch (Exception ex)
//     {
//         return Results.Problem(ex.Message);
//     }
// });

#endregion

#region Database Endpoints

todoGroup.MapGet("/", async (AppDbContext db) =>
{
    var todos = await db.Todos.ToListAsync();

    var todoGetDtos = todos.Select(t =>
                             new TodoGetDto(
                                t.Id,
                                t.Title,
                                t.IsCompleted
                            ));
    return todoGetDtos.Count() == 0 ? Results.NotFound() : Results.Ok(todoGetDtos);
 
})
.RequireAuthorization();

;todoGroup.MapGet("/{id}", async (int id, AppDbContext db) =>
{
    var todo = await db.Todos.FindAsync(id);
    if (todo is null) return Results.NotFound();

    return Results.Ok(
        new TodoGetDto(todo.Id, todo.Title, todo.IsCompleted)
    );
});

todoGroup.MapPost("/", async (TodoPostDto dto, AppDbContext db) =>
{
    var todo = new TodoItem
    {
        Title = dto.Title,
        IsCompleted = false,
        Created = DateTime.UtcNow
    };

    db.Todos.Add(todo);
    await db.SaveChangesAsync();

    var result = new TodoGetDto(todo.Id, todo.Title, todo.IsCompleted);
    return Results.Created($"/api/todos/{todo.Id}", result);
});

todoGroup.MapPut("/{id}", async (int id, TodoPutDto dto, AppDbContext db) =>
{
    var todo = await db.Todos.FindAsync(id);
    if (todo is null) return Results.NotFound();

    todo.Title = dto.Title;
    todo.IsCompleted = dto.IsCompleted;
    await db.SaveChangesAsync();

    return Results.Ok(
        new TodoGetDto(todo.Id, todo.Title, todo.IsCompleted)
    );
});

todoGroup.MapDelete("/{id}", async (int id, AppDbContext db) =>
{
    var todo = await db.Todos.FindAsync(id);
    if (todo is null) return Results.NotFound();

    db.Todos.Remove(todo);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

#endregion

#region Authentication Endpoint

app.MapPost("/api/login", (LoginDto dto, IConfiguration configuration) =>
{
    if (dto.Username != "admin" || dto.Password != "password") return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, dto.Username)
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: configuration["Jwt:Issuer"],
        audience: configuration["Jwt:Audience"],
        claims: claims,
        expires: DateTime.UtcNow.AddHours(1),
        signingCredentials: credentials);

    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new { Token = tokenString});
});

#endregion

app.Run();