using UnityEngine;

namespace WKMPMod.MK_Component;

public class MK_RemoteEntity :MonoBehaviour{
	public ulong PlayerId;
	public float AllActive = 1;
	public float HammerActive = 1;
	public float RebarActive = 1;
	public float ReturnRebarActive = 1;
	public float RebarExplosionActive = 1;
	public float ExplosionActive = 1;
	public float PitonActive = 1;
	public float FlareActive = 1;
	public float IceActive = 1;
	public float OtherActive = 1;
	public GameObject DamageObject;
}
