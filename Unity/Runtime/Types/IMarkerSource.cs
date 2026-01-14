namespace UnityJigs.Types
{
    /// <summary>
    /// Provides marker data for a <c>MarkerTrackDrawer</c>.
    /// Implement this in your editor to allow the track to display and edit time markers.
    /// </summary>
    public interface IMarkerTrackSource
    {
        /// <summary>Number of markers available.</summary>
        int GetMarkerCount();

        /// <summary>Gets the normalized time (0–1) for the given marker index.</summary>
        float GetMarkerTime(int index);

        /// <summary>Called when a new marker should be added.</summary>
        void AddMarker(float normalizedTime);

        /// <summary>Called when a marker should be removed.</summary>
        void RemoveMarker(int index);

        /// <summary>Called when a marker's time is changed by user interaction.</summary>
        void UpdateMarkerTime(int index, float newNormalizedTime);

        /// <summary>
        /// Optional label to show near the marker (e.g., event name or index).
        /// Return null or empty string for no label.
        /// </summary>
        string? GetMarkerLabel(int index) => null;

        /// <summary>
        /// Suggested time (0–1) for new markers; typically the preview playhead.
        /// </summary>
        float GetSuggestedNewMarkerTime() => .5f;
    }
}
