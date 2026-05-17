using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Escalated.Migrations;

/// <summary>Design-time EF model baseline (shared fluent API with <see cref="EscalatedDbContext"/>).</summary>
[DbContext(typeof(EscalatedDbContext))]
public partial class EscalatedDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        EscalatedModelConfiguration.Configure(modelBuilder);

#pragma warning disable CS0618 // Mirrors EF-generated snapshots
        modelBuilder.HasAnnotation("ProductVersion", "9.0.15");
#pragma warning restore CS0618
    }
}
