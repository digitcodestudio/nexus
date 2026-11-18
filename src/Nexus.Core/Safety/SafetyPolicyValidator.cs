using Nexus.Core.Contracts;
using Nexus.Models;

namespace Nexus.Core.Safety;

public sealed class SafetyPolicyValidator : ISafetyPolicyValidator
{
    public OperationResult ValidateServiceMutation(string serviceName, ServiceDependencyGraph dependencies, bool userConfirmed)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return OperationResult.Fail("Service name cannot be empty.", "INVALID_SERVICE");
        }

        if (ProtectedLists.IsImmutableCriticalService(serviceName))
        {
            return OperationResult.Fail(
                $"Service '{serviceName}' is a protected critical Windows service and cannot be disabled or stopped by NEXUS.",
                "CRITICAL_SERVICE_PROTECTED");
        }

        if (dependencies.HasCriticalDependents)
        {
            return OperationResult.Fail(
                $"Service '{serviceName}' has critical Windows dependent services and cannot be safely modified.",
                "CRITICAL_DEPENDENT_BLOCKED");
        }

        if (!userConfirmed)
        {
            return OperationResult.Fail(
                $"Explicit user confirmation is required before modifying service '{serviceName}'.",
                "CONFIRMATION_REQUIRED");
        }

        return OperationResult.Ok("Service mutation passed safety validation.");
    }

    public OperationResult ValidateProcessAction(ProcessInfo process, SafeActionType action, bool userConfirmed)
    {
        if (action == SafeActionType.Ignore || action == SafeActionType.Notify)
        {
            return OperationResult.Ok("Non-intrusive action permitted.");
        }

        if (process.IsImmutableSystem || ProtectedLists.IsImmutableSystemProcess(process.ProcessName))
        {
            return OperationResult.Fail(
                $"Process '{process.ProcessName}' (PID {process.ProcessId}) is an immutable Windows system process.",
                "IMMUTABLE_SYSTEM_PROCESS");
        }

        if (process.IsProtected && action is SafeActionType.GracefulClose or SafeActionType.Suspend)
        {
            return OperationResult.Fail(
                $"Process '{process.ProcessName}' is protected by the Application Whitelist.",
                "PROTECTED_APPLICATION");
        }

        if (process.IsForeground && action is SafeActionType.GracefulClose or SafeActionType.Suspend or SafeActionType.Minimize)
        {
            return OperationResult.Fail(
                $"Process '{process.ProcessName}' is currently in the foreground and will not be interrupted.",
                "FOREGROUND_ACTIVE");
        }

        if ((action is SafeActionType.GracefulClose or SafeActionType.Suspend) && !userConfirmed)
        {
            return OperationResult.Fail(
                $"Action '{action}' on '{process.ProcessName}' requires explicit user confirmation.",
                "CONFIRMATION_REQUIRED");
        }

        return OperationResult.Ok("Process action passed safety validation.");
    }
}

