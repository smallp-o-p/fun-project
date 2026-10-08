namespace FunProject.Engineering;

public record struct ManufacturingJob(ManufacturingRecipe Project, long StartedAtTick, long CompletesAtTick);
