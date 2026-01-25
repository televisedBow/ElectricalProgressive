namespace ElectricalProgressive.Utils;

/// <summary>
/// Tracks burnout state from EParams array.
/// Consolidates duplicated burnout tracking logic used across behavior classes.
/// </summary>
public class BurnoutTracker
{
    /// <summary>
    /// Whether any face has burned out
    /// </summary>
    public bool HasBurnout { get; private set; }

    /// <summary>
    /// Whether any face is accumulating damage toward burnout
    /// </summary>
    public bool PrepareBurnout { get; private set; }

    /// <summary>
    /// Updates burnout state from AllEparams array.
    /// Returns true if any state changed (caller should MarkDirty).
    /// </summary>
    /// <param name="allEparams">Array of EParams for all 6 block faces</param>
    /// <returns>True if state changed and block should be marked dirty</returns>
    public bool Update(EParams[]? allEparams)
    {
        if (allEparams is null)
            return false;

        bool anyBurnout = false;
        bool anyPrepareBurnout = false;

        foreach (var eParam in allEparams)
        {
            if (eParam.burnout)
                anyBurnout = true;
            if (eParam.ticksBeforeBurnout > 0)
                anyPrepareBurnout = true;
        }

        bool stateChanged = false;

        if (anyBurnout != HasBurnout)
        {
            HasBurnout = anyBurnout;
            stateChanged = true;
        }

        if (anyPrepareBurnout != PrepareBurnout)
        {
            PrepareBurnout = anyPrepareBurnout;
            stateChanged = true;
        }

        return stateChanged;
    }
}
