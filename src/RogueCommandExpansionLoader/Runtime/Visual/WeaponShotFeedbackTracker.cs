namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>Tracks a native shot clock whose idle sentinel may itself be negative.</summary>
    public struct WeaponShotFeedbackTracker
    {
        private bool _hasSample;
        private float _lastTimestamp;

        public bool Observe(float timestamp)
        {
            if (float.IsNaN(timestamp) || float.IsInfinity(timestamp)) return false;
            if (!_hasSample)
            {
                _hasSample = true;
                _lastTimestamp = timestamp;
                return false;
            }
            bool fired = timestamp > _lastTimestamp;
            _lastTimestamp = timestamp;
            return fired;
        }

        public void Reset()
        {
            _hasSample = false;
            _lastTimestamp = 0f;
        }
    }
}
