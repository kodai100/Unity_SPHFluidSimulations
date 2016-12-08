using UnityEngine;
using System.Collections;

public class Faller : MonoBehaviour {

    private bool up = false;
    public float speed = 1.0f;
    public float startTime = 10f;
    private Vector3 length;

    private float time = 0;
    private Vector3 min, max;

	// Use this for initialization
	void Start () {
        up = false;
        min = GetComponent<SPH3D>().MIN;
        max = GetComponent<SPH3D>().MAX;
        length = max - min;
    }
	
	// Update is called once per frame
	void Update () {
        time += Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.I) && !up){
            Vector3 fourthLength = length / 4;
            Vector3 l = new Vector3(1.0f, 1.0f, 1.0f);
            GetComponent<SPH3D>().MIN = min + fourthLength;
            GetComponent<SPH3D>().MAX = min + fourthLength + l;
            up = true;
        }

        if (Input.GetKeyDown(KeyCode.F) && up) {
            GetComponent<SPH3D>().MIN = min;
            GetComponent<SPH3D>().MAX = max;
            up = false;
        }

        if (Time.time > startTime && !up){
            float res = min.x + length.x / 4 + length.x / 4 * Mathf.Sin(speed * time);
            GetComponent<SPH3D>().MIN.x = res;
        } else if (up){
            //GetComponent<GPUSPHParticleSystem>().MIN.x = min.x;
        }
        
    }
    
}
