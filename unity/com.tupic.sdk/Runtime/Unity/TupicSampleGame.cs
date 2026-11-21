// The Tupic sample: collect the stars. Arrow keys / WASD or pointer to move; 5 stars per level, 3 levels; the timer
// ends the game. Scores, levels and the game end go to Tupic through TupicGame. A text-stored model (glTF) spins in
// the corner, built from the same document the web engine reads.
using UnityEngine;

namespace Tupic.Unity
{
    [RequireComponent(typeof(TupicGame))]
    public sealed class TupicSampleGame : MonoBehaviour
    {
        public TextAsset model;                  // glTF text (Tupic/Import OBJ as Text Model)
        public int starsPerLevel = 5, levels = 3;
        public float secondsPerLevel = 30f;

        static readonly Color Bg = new Color32(10, 10, 26, 255), Accent = new Color32(99, 102, 241, 255), Star = new Color32(250, 204, 21, 255);
        enum State { Start, Playing, Won, Lost }
        State _state = State.Start;
        TupicGame _tupic;
        Transform _player, _star, _logo;
        int _collected;
        float _timeLeft;
        GUIStyle _big, _small;

        void Start()
        {
            _tupic = GetComponent<TupicGame>();
            var cam = Camera.main;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Bg; cam.orthographic = true; cam.orthographicSize = 5;
            _player = Make(PrimitiveType.Cube, Accent, 0.8f).transform;
            _star = Make(PrimitiveType.Sphere, Star, 0.5f).transform;
            _star.gameObject.SetActive(false);
            if (model != null)
            {
                var go = new GameObject("TupicModel", typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().mesh = TupicModelMesh.FromText(model.text, model.name);
                go.GetComponent<MeshRenderer>().material = Mat(Accent);
                go.transform.position = new Vector3(6.5f, 3.2f, 0); go.transform.localScale = Vector3.one * 1.2f;
                _logo = go.transform;
            }
            var light = new GameObject("Light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, -30, 0);
        }

        static Material Mat(Color c) { var m = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color")); m.color = c; return m; } // both are Always Included by the Tupic build
        // No colliders (CreatePrimitive adds one, and WebGL strips the physics module): a built-in mesh and a renderer.
        static GameObject Make(PrimitiveType t, Color c, float s)
        {
            var g = new GameObject(t.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
            g.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(t == PrimitiveType.Sphere ? "Sphere.fbx" : "Cube.fbx");
            g.transform.localScale = Vector3.one * s; g.GetComponent<Renderer>().material = Mat(c); return g;
        }

        void NewStar() { _star.position = new Vector3(Random.Range(-7f, 7f), Random.Range(-4f, 3.5f), 0); _star.gameObject.SetActive(true); }

        void Begin()
        {
            _state = State.Playing; _collected = 0; _timeLeft = secondsPerLevel; _player.position = Vector3.zero; NewStar();
        }

        bool Pressed() => Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0);

        void Update()
        {
            if (_logo) _logo.Rotate(20 * Time.deltaTime, 45 * Time.deltaTime, 0);
            if (_state != State.Playing) { if (Pressed()) { if (_state != State.Start) _tupic.NewGame(); Begin(); } return; }
            var move = new Vector3(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"), 0);
            if (Input.GetMouseButton(0))
            {
                var p = Camera.main.ScreenToWorldPoint(Input.mousePosition); p.z = 0;
                move = (p - _player.position).normalized;
            }
            _player.position = Vector3.ClampMagnitude(_player.position + move * 7f * Time.deltaTime, 9f);
            _player.Rotate(0, 0, 90 * Time.deltaTime);
            if (Vector3.Distance(_player.position, _star.position) < 0.7f)
            {
                _collected++; _tupic.AddScore(10 * _tupic.Level);
                if (_collected >= starsPerLevel)
                {
                    _tupic.CompleteLevel();
                    if (_tupic.Level > levels) { _state = State.Won; _star.gameObject.SetActive(false); _tupic.EndGame("victory"); return; }
                    _collected = 0; _timeLeft = secondsPerLevel;
                }
                NewStar();
            }
            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0) { _state = State.Lost; _star.gameObject.SetActive(false); _tupic.EndGame("gameover"); }
        }

        void OnGUI()
        {
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(28, Screen.height / 12), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _big.normal.textColor = Color.white;
                _small = new GUIStyle(_big) { fontSize = Mathf.Max(16, Screen.height / 28), fontStyle = FontStyle.Normal };
                _small.normal.textColor = new Color32(165, 180, 252, 255);
            }
            var w = Screen.width; var h = Screen.height;
            GUI.Label(new Rect(16, 8, w, 40), $"TUPIC  ·  Level {Mathf.Min(_tupic.Level, levels)}/{levels}  ·  Score {_tupic.Score}  ·  {Mathf.CeilToInt(Mathf.Max(0, _timeLeft))}s", _small.Left());
            string title = _state == State.Start ? "TUPIC STARS" : _state == State.Won ? "VICTORY" : _state == State.Lost ? "GAME OVER" : null;
            if (title != null)
            {
                GUI.Label(new Rect(0, h * 0.30f, w, h * 0.15f), title, _big);
                GUI.Label(new Rect(0, h * 0.48f, w, h * 0.08f), _state == State.Start ? "Collect the stars · click or press Space" : $"Score {_tupic.Score} · click or press Space to play again", _small);
            }
        }
    }

    static class GuiStyleExt { public static GUIStyle Left(this GUIStyle s) => new GUIStyle(s) { alignment = TextAnchor.UpperLeft }; }
}
