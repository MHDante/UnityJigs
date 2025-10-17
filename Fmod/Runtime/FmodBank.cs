using System;
using UnityEngine;
using FMOD.Studio;
using FMODUnity;

namespace UnityJigs.Fmod
{
    /// <summary>
    /// Serializable wrapper around an FMOD bank path.
    /// Provides helper methods that forward to RuntimeManager.
    /// </summary>
    [Serializable]
    public struct FmodBank
    {
        [SerializeField, BankRef]
        private string BankName;

        public FmodBank(string name) => BankName = name;

        /// <summary> The name or path of the FMOD bank. </summary>
        public string Name => BankName;

        /// <summary> True if this wrapper points to a non-empty bank name. </summary>
        public bool IsValid => !string.IsNullOrEmpty(BankName);

        /// <summary> Loads this bank (optionally including sample data). </summary>
        public void Load(bool loadSamples = false) => RuntimeManager.LoadBank(BankName, loadSamples);
        public void LoadAndWait(bool loadSamples = false)
        {
            Load(loadSamples);
            RuntimeManager.WaitForAllSampleLoading();
        }

        /// <summary> Unloads this bank. </summary>
        public void Unload() => RuntimeManager.UnloadBank(BankName);

        /// <summary> Returns true if this bank has been loaded. </summary>
        public bool IsLoaded() => RuntimeManager.HasBankLoaded(BankName);

        /// <summary> Returns true if all sample data for this bank is loaded. </summary>
        public bool IsSampleDataLoaded()
        {
            if (!IsValid) return false;
            var bank = GetBank();
            bank.getSampleLoadingState(out var state);
            return state == LOADING_STATE.LOADED;
        }

        /// <summary> Blocks until all sample data is loaded for this bank. </summary>
        public void WaitForSampleLoading() => RuntimeManager.WaitForAllSampleLoading();

        /// <summary> Retrieves the FMOD.Studio.Bank handle for this bank. </summary>
        public Bank GetBank()
        {
            // Query through RuntimeManager internals (throws if missing)
            RuntimeManager.StudioSystem.getBank(BankName, out var bank);
            return bank;
        }

        /// <summary> Loads only the sample data for this bank (bank must already be loaded). </summary>
        public void LoadSampleData()
        {
            var bank = GetBank();
            bank.loadSampleData();
        }

        /// <summary> Unloads only the sample data for this bank. </summary>
        public void UnloadSampleData()
        {
            var bank = GetBank();
            bank.unloadSampleData();
        }

        public override string ToString() => BankName ?? "<null>";

        // Implicit conversions for convenience
        public static implicit operator string(FmodBank b) => b.BankName;
        public static implicit operator FmodBank(string s) => new(s);
    }
}
