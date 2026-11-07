using LuceneServerNET.Admin.Services.Instances;
using LuceneServerNET.Client;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace LuceneServerNET.Admin.Components.Shared;

/// <summary>Base for pages of a single instance (/i/{InstanceName}/...): resolves the instance, client and status.</summary>
public abstract class InstancePageBase : ComponentBase
{
    [Inject] protected InstanceRegistry Registry { get; set; } = default!;
    [Inject] protected ISnackbar Snackbar { get; set; } = default!;
    [Inject] protected IDialogService DialogService { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    [Parameter] public string InstanceName { get; set; } = "";

    protected ServerInstance? Instance { get; private set; }
    protected LuceneServerV2Client? Client { get; private set; }
    protected InstanceStatus Status { get; private set; } = new();
    protected bool Loading { get; set; } = true;

    protected string InstanceUrl => $"i/{Uri.EscapeDataString(InstanceName)}";

    private string? _loadedFor;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor == InstanceName)
        {
            return;
        }
        _loadedFor = InstanceName;

        Loading = true;
        Instance = await Registry.GetAsync(InstanceName);
        if (Instance != null)
        {
            Client = Registry.CreateClient(Instance);
            Status = await InstanceRegistry.ProbeAsync(Client);
            if (Status.Online)
            {
                await LoadAsync();
            }
        }
        Loading = false;
    }

    /// <summary>Loads the page data (instance is online).</summary>
    protected virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Runs an action and shows errors / success as snackbar.</summary>
    protected async Task<bool> RunAsync(Func<Task> action, string? successMessage = null)
    {
        try
        {
            await action();
            if (successMessage != null)
            {
                Snackbar.Add(successMessage, Severity.Success);
            }
            return true;
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
            return false;
        }
    }

    protected async Task<bool> ConfirmAsync(string title, string message, string yesText = "Yes")
        => await DialogService.ShowMessageBoxAsync(title, message, yesText: yesText, cancelText: "Cancel") == true;
}
