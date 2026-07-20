using Microsoft.EntityFrameworkCore;
using MiniMonitor.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// A connection string vem do appsettings.json, mas qualquer variável de
// ambiente chamada ConnectionStrings__Postgres sobrescreve o valor sem
// precisar tocar em arquivo versionado.
var connectionString = builder.Configuration.GetConnectionString("Postgres");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

// Aplica as migrations pendentes no startup. Deixa o projeto rodar com um
// comando só depois de subir o Postgres, ao custo de a API mexer no schema
// sozinha, o que não seria aceitável em produção.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();
