namespace RogueCommandExpansionLoader.Runtime
{
    public static class InspectorTogglePolicy
    {
        public static bool Resolve(bool isOpen, bool togglePressed, bool closePressed)
        {
            if (closePressed)
            {
                return false;
            }

            return togglePressed ? !isOpen : isOpen;
        }
    }
}
