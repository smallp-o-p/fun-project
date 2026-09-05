namespace FunProject.Engineering;

public record struct ManufacturingJob(ManufacturingProject Project, long StartedAtTick, long CompletesAtTick);
