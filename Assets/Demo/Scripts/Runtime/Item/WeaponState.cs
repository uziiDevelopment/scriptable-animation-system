namespace Demo.Scripts.Runtime.Item
{
    public enum WeaponState
    {
        Ready,      // Can fire, has ammo
        Empty,      // Magazine empty, cannot fire
        Reloading   // Currently reloading
    }
}
