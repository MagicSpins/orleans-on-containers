﻿using GrainInterfaces;
using Microsoft.Extensions.Logging;

namespace Client.Services;

public class GrainObserverManager : ISubscriptionManager
{
    private const string _generalSubscriptionFailureMessage = "An error occurred during the subscription process.";
    private readonly IChatObserver _chatObserver;
    private readonly IClusterClient _clusterClient;
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<GrainObserverManager> _logger;
    private readonly IResubscriber<GrainSubscription> _resubscriber;
    private GrainObserverManagerState _state = new();

    // This class has a lot of dependencies...
    public GrainObserverManager(
        IChatObserver chatObserver,
        IClusterClient clusterClient,
        IGrainFactory grainFactory,
        ILogger<GrainObserverManager> logger,
        IResubscriber<GrainSubscription> resubscriber)
    {
        _chatObserver = chatObserver;
        _clusterClient = clusterClient;
        _grainFactory = grainFactory;
        _logger = logger;
        _resubscriber = resubscriber;
    }

    public async Task<Result> Subscribe(string grainId)
    {
        _logger.LogDebug("Attempting to subscribe to {Grain}.", grainId);

        if (_state.IsSubscribed)
        {
            _logger.LogDebug("Failed to subscribe to {Grain}. Client is already subscribed to {Grain}.", grainId, _state.GrainId);

            return Result.Failure("A subscription is already being managed. Unsubscribe first before registering a new subscription.");
        }

        var stateSet = SetState(grainId);

        if (!stateSet)
        {
            return Result.Failure(_generalSubscriptionFailureMessage);
        }

        var subscribed = await Subscribe();

        if (!subscribed)
        {
            return Result.Failure(_generalSubscriptionFailureMessage);
        }

        var registeredResubscriber = await RegisterResubscriber();

        if (!registeredResubscriber)
        {
            return Result.Failure(_generalSubscriptionFailureMessage);
        }

        _logger.LogDebug("Successfully subscribed to {Grain}.", grainId);

        return Result.Success();
    }

    public async Task<Result> Unsubscribe(string grainId)
    {
        _logger.LogDebug("Attempting to subscribe to {Grain}.", grainId);

        if (!_state.IsSubscribed)
        {
            _logger.LogDebug("Failed to unsubscribe to {Grain}. No existing subscription exists.", grainId);

            return Result.Failure("No subscription currently exists.");
        }

        var grain = _clusterClient.GetGrain<IChatGrain>(_state.GrainId);
        // _state.Reference cannot be null here if _state.IsSubscribed is true
        await grain.Unsubscribe(_state.Reference!);
        _state.Clear();
        await _resubscriber.Clear();

        _logger.LogDebug("Successfully unsubscribed from {Grain}.", grainId);

        return Result.Success();
    }

    private async Task<bool> RegisterResubscriber()
    {
        try
        {
            await _resubscriber.Register(_state, SubscribeToGrain);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register resubscription delegate for {Grain}.", _state.GrainId);
        }

        return false;
    }

    private bool SetState(string grainId)
    {
        try
        {
            _state.Set(grainId, _grainFactory.CreateObjectReference<IChatObserver>(_chatObserver));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update state.");
        }

        return false;
    }

    private async Task<bool> Subscribe()
    {
        try
        {
            await SubscribeToGrain(_state);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to {Grain}.", _state.GrainId);
            _state.Clear();
        }

        return false;
    }

    private async Task SubscribeToGrain(GrainSubscription grainSubscription)
    {
        var grain = _clusterClient.GetGrain<IChatGrain>(grainSubscription.GrainId);
        await grain.Subscribe(grainSubscription.Reference!);
    }
}

public class GrainObserverManagerState : GrainSubscription
{
    public bool IsSubscribed =>
        !string.IsNullOrEmpty(GrainId) &&
        Reference != null;

    public void Clear()
    {
        GrainId = null;
        Reference = null;
    }

    public void Set(string grainId, IChatObserver reference)
    {
        GrainId = grainId;
        Reference = reference;
    }
}
