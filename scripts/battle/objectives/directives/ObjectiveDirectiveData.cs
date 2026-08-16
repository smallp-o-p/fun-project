using Godot;

namespace FunProject.Battle;

// What an objective's completion/failure MEANS. Authored per objective instance; follow-ups
// queued by QueueDirectiveData carry their own instance-bound directives — outcomes bind on
// the queued objective, never on the queue edge. A null directive is silent: the flip is
// recorded (state + events) and nothing else happens.
[GlobalClass]
public abstract partial class ObjectiveDirectiveData : Resource
{
}
