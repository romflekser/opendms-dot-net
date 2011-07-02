﻿
namespace OpenDMS.Storage.Providers
{
    public enum EngineActionType
    {
        Preparing,
        Getting,
        GettingGroups,
        CreatingNewResource,
        CreatingNewVersion,
        UpdatingResource,
        RecoveringFromError,
        RecoveringFromTimeout,
        Reverting,
        SessionLookup,
        CheckingPermissions,
        // No need to send a complete action as completion is signaled by Error, Timeout or Completion
        // Complete
    }
}
