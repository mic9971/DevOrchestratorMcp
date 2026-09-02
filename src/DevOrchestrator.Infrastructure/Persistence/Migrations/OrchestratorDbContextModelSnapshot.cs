using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevOrchestrator.Infrastructure.Persistence.Migrations;

[DbContext(typeof(OrchestratorDbContext))]
public sealed class OrchestratorDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.30");
        OrchestratorModel.Configure(modelBuilder);
    }
}
