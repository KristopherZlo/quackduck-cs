using System;
using System.Collections.Generic;

namespace QuackDuck;

/// <summary>
/// Lightweight finite state machine with a fluent builder tailored for the pet logic.
/// </summary>
internal sealed class PetStateMachine
{
    private readonly Dictionary<string, PetState> states;
    private PetState current;

    internal PetStateMachine(Dictionary<string, PetState> states, string initialState)
    {
        this.states = states;
        current = states[initialState];
        current.Enter();
    }

    internal string CurrentState => current.Name;

    internal void Update()
    {
        current.Update();
        var next = current.GetNextState();
        if (next is not null)
        {
            Switch(next);
        }
    }

    internal void ForceState(string targetState)
    {
        Switch(targetState);
    }

    private void Switch(string targetState)
    {
        if (!states.TryGetValue(targetState, out var next))
        {
            throw new InvalidOperationException($"State '{targetState}' is not registered in the machine.");
        }

        if (ReferenceEquals(current, next))
        {
            return;
        }

        current.Exit();
        current = next;
        current.Enter();
    }
}

internal sealed class PetStateMachineBuilder
{
    private readonly Dictionary<string, PetStateBuilder> states = new();
    private string? initialState;

    private PetStateMachineBuilder()
    {
    }

    internal static PetStateMachineBuilder Create() => new();

    internal PetStateBuilder State(string name)
    {
        var builder = new PetStateBuilder(name, this);
        states[name] = builder;
        initialState ??= name;
        return builder;
    }

    internal PetStateMachineBuilder WithInitialState(string name)
    {
        initialState = name;
        return this;
    }

    internal PetStateMachine Build()
    {
        if (states.Count == 0)
        {
            throw new InvalidOperationException("Cannot build a state machine without states.");
        }

        var materialized = new Dictionary<string, PetState>();
        foreach (var (stateName, builder) in states)
        {
            materialized[stateName] = builder.Build();
        }

        var initial = initialState ?? throw new InvalidOperationException("Initial state is not specified.");
        if (!materialized.ContainsKey(initial))
        {
            throw new InvalidOperationException($"Initial state '{initial}' was not defined.");
        }

        return new PetStateMachine(materialized, initial);
    }
}

internal sealed class PetStateBuilder
{
    private readonly string name;
    private readonly PetStateMachineBuilder parent;
    private Action? onEnter;
    private Action? onUpdate;
    private Action? onExit;
    private readonly List<PetStateTransition> transitions = new();

    internal PetStateBuilder(string name, PetStateMachineBuilder parent)
    {
        this.name = name;
        this.parent = parent;
    }

    internal PetStateBuilder OnEnter(Action action)
    {
        onEnter = action;
        return this;
    }

    internal PetStateBuilder OnUpdate(Action action)
    {
        onUpdate = action;
        return this;
    }

    internal PetStateBuilder OnExit(Action action)
    {
        onExit = action;
        return this;
    }

    internal PetStateTransitionBuilder When(Func<bool> condition)
    {
        return new PetStateTransitionBuilder(this, condition);
    }

    internal PetStateBuilder When(Func<bool> condition, string targetState)
    {
        AddTransition(condition, targetState);
        return this;
    }

    internal PetStateMachineBuilder EndState() => parent;

    internal PetState Build() => new(name, onEnter, onUpdate, onExit, transitions.ToArray());

    internal void AddTransition(Func<bool> condition, string targetState)
    {
        transitions.Add(new PetStateTransition(condition, targetState));
    }
}

internal sealed class PetStateTransitionBuilder
{
    private readonly PetStateBuilder parent;
    private readonly Func<bool> condition;

    internal PetStateTransitionBuilder(PetStateBuilder parent, Func<bool> condition)
    {
        this.parent = parent;
        this.condition = condition;
    }

    internal PetStateBuilder GoTo(string targetState)
    {
        parent.AddTransition(condition, targetState);
        return parent;
    }
}

internal sealed class PetState
{
    private readonly Action? onEnter;
    private readonly Action? onUpdate;
    private readonly Action? onExit;
    private readonly IReadOnlyList<PetStateTransition> transitions;

    internal PetState(string name, Action? onEnter, Action? onUpdate, Action? onExit, IReadOnlyList<PetStateTransition> transitions)
    {
        Name = name;
        this.onEnter = onEnter;
        this.onUpdate = onUpdate;
        this.onExit = onExit;
        this.transitions = transitions;
    }

    internal string Name { get; }

    internal void Enter() => onEnter?.Invoke();

    internal void Update() => onUpdate?.Invoke();

    internal void Exit() => onExit?.Invoke();

    internal string? GetNextState()
    {
        foreach (var transition in transitions)
        {
            if (transition.ShouldTransition())
            {
                return transition.TargetState;
            }
        }

        return null;
    }
}

internal sealed class PetStateTransition
{
    private readonly Func<bool> condition;

    internal PetStateTransition(Func<bool> condition, string targetState)
    {
        this.condition = condition;
        TargetState = targetState;
    }

    internal string TargetState { get; }

    internal bool ShouldTransition() => condition();
}
