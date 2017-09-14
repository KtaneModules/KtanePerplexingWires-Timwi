using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PerplexingWires;
using UnityEngine;

using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of Perplexing Wires
/// Created by Timwi
/// </summary>
public class PerplexingWiresModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;
    public KMSelectable MainSelectable;

    public Texture[] Arrows;
    public Texture[] Stars;
    public Material[] LedMaterials;
    public Material[] WireMaterials;

    public MeshRenderer[] ArrowMeshes;
    public MeshRenderer[] StarMeshes;
    public MeshRenderer[] LedMeshes;

    [Flags]
    enum Arrow
    {
        Up = 0 << 0,
        Left = 1 << 0,
        Down = 2 << 0,
        Right = 3 << 0,
        DirectionMask = 3 << 0,

        Red = 0 << 2,
        Green = 1 << 2,
        Blue = 2 << 2,
        Yellow = 3 << 2,
        Purple = 4 << 2,
        ColorMask = 7 << 2
    }
    private static string arrowColorStr(Arrow arrow)
    {
        // Workaround because “Up” and “Red” have the same value (0) so .ToString() might stringify it to “Up”
        var color = arrow & Arrow.ColorMask;
        if (color == Arrow.Red)
            return "Red";
        return color.ToString();
    }
    private static string arrowDirStr(Arrow arrow)
    {
        // Workaround because “Up” and “Red” have the same value (0) so .ToString() might stringify it to “Red”
        var dir = arrow & Arrow.DirectionMask;
        if (dir == Arrow.Up)
            return "Up";
        return dir.ToString();
    }

    [Flags] enum WireColor { Red, Yellow, Blue, White, Green, Orange, Purple, Black }

    private Arrow[] _arrows;
    private bool[] _filledStars;
    private bool[] _ledsOn;
    private GameObject _wireCopper;

    enum CutRule { DontCut, CutFirst, CutLast, Cut }
    private static string cutRuleStr(CutRule rule)
    {
        switch (rule)
        {
            case CutRule.DontCut: return "don’t cut";
            case CutRule.CutFirst: return "cut first";
            case CutRule.CutLast: return "cut last";
            case CutRule.Cut: return "cut";
        }
        return null;
    }

    sealed class WireInfo
    {
        public int TopConnector;
        public int BottomConnector;
        public WireColor Color;
        public int Level;
        public CutRule CutRule;
        public string Reason;
        public bool HasBeenCut;
        public MeshFilter MeshFilter;
        public MeshFilter HighlightMeshFilter;
        public KMSelectable Selectable;
        public Mesh CutMesh;
        public Mesh CutHighlightMesh;
        public Mesh CopperMesh;
    }
    private WireInfo[] _wires = new WireInfo[6];

    private static int _moduleIdCounter = 1;
    private int _moduleId;

    void Start()
    {
        _moduleId = _moduleIdCounter++;
        StartCoroutine(Initialize());
    }

    private IEnumerator Initialize()
    {
        yield return new WaitForSeconds(Rnd.Range(.01f, .1f));

        var retries = 0;
        retry:

        //
        // STEP 1: Decide on all the arrows, filled stars and LED states
        //
        _arrows = new Arrow[ArrowMeshes.Length];
        for (int i = 0; i < ArrowMeshes.Length; i++)
        {
            _arrows[i] = (Arrow) ((Rnd.Range(0, 4) << 0) | (Rnd.Range(0, 5) << 2));
            ArrowMeshes[i].material.mainTexture = Arrows[(int) _arrows[i]];
        }

        _filledStars = new bool[StarMeshes.Length];
        for (int i = 0; i < StarMeshes.Length; i++)
        {
            _filledStars[i] = Rnd.Range(0, 2) != 0;
            StarMeshes[i].material.mainTexture = Stars[_filledStars[i] ? 1 : 0];
        }

        _ledsOn = new bool[LedMeshes.Length];
        for (int i = 0; i < LedMeshes.Length; i++)
        {
            _ledsOn[i] = Rnd.Range(0, 2) != 0;
            LedMeshes[i].material = LedMaterials[_ledsOn[i] ? 1 : 0];
        }

        //
        // STEP 2: Assign the wires to their connectors and decide their colors
        //
        var shuffledBottom = Enumerable.Range(0, 6).ToList().Shuffle();
        for (int i = 0; i < 6; i++)
        {
            _wires[i] = new WireInfo
            {
                TopConnector = i < 4 ? i : Rnd.Range(0, 4),
                BottomConnector = shuffledBottom[i],
                Color = (WireColor) Rnd.Range(0, 8),
                Level = 1,
                HasBeenCut = false
            };
            for (int j = 0; j < i; j++)
                if ((_wires[j].TopConnector > _wires[i].TopConnector && _wires[j].BottomConnector < _wires[i].BottomConnector) ||
                    (_wires[j].TopConnector < _wires[i].TopConnector && _wires[j].BottomConnector > _wires[i].BottomConnector))
                    _wires[i].Level = Math.Max(_wires[i].Level, _wires[j].Level + 1);
        }

        //
        // STEP 3: Determine the solution. Make sure that at least one wire needs to be cut.
        //
        const string rules = "LWIPMVIFIUCCFRHHTVUDLRJBQWBDJTQD";
        var colorsForRedRule = new[] { WireColor.Red, WireColor.Yellow, WireColor.Blue, WireColor.White };
        for (int i = 0; i < 6; i++)
        {
            var applicable = new List<string>();
            var rule = 0;
            // Blue: The wire crosses over another wire.
            if (_wires.Any(w => (w.BottomConnector > _wires[i].BottomConnector && w.TopConnector < _wires[i].TopConnector) || (w.BottomConnector < _wires[i].BottomConnector && w.TopConnector > _wires[i].TopConnector)))
            {
                rule += 1;
                applicable.Add("Blue");
            }
            // Yellow: The wire’s star is black.
            if (_filledStars[_wires[i].TopConnector])
            {
                rule += 2;
                applicable.Add("Yellow");
            }
            // Green: The wire’s position on the bottom is even.
            if (_wires[i].BottomConnector % 2 != 0)
            {
                rule += 4;
                applicable.Add("Green");
            }
            // Red: The wire is red, yellow, blue, or white.
            if (colorsForRedRule.Contains(_wires[i].Color))
            {
                rule += 8;
                applicable.Add("Red");
            }
            var arrowColor = _arrows[_wires[i].BottomConnector] & Arrow.ColorMask;
            // Orange: The wire shares the same color as its arrow.
            if ((_wires[i].Color == WireColor.Red && arrowColor == Arrow.Red) ||
                (_wires[i].Color == WireColor.Green && arrowColor == Arrow.Green) ||
                (_wires[i].Color == WireColor.Blue && arrowColor == Arrow.Blue) ||
                (_wires[i].Color == WireColor.Yellow && arrowColor == Arrow.Yellow) ||
                (_wires[i].Color == WireColor.Purple && arrowColor == Arrow.Purple))
            {
                rule += 16;
                applicable.Add("Orange");
            }

            _wires[i].Reason = string.Format("{0} = {1}", applicable.Count == 0 ? "none" : applicable.JoinString("+"), rules[rule]);

            var dir = (_arrows[_wires[i].BottomConnector] & Arrow.DirectionMask);
            switch (rules[rule])
            {
                // C: Cut the wire.
                case 'C': _wires[i].CutRule = CutRule.Cut; break;
                // F: Always cut the wire, but only cut it first.
                case 'F': _wires[i].CutRule = CutRule.CutFirst; break;
                // L: Always cut the wire, but only cut it last.
                case 'L': _wires[i].CutRule = CutRule.CutLast; break;
                // W: Cut the wire if more of the LEDs are on than off.
                case 'W': _wires[i].CutRule = _ledsOn.Count(l => l) > 1 ? CutRule.Cut : CutRule.DontCut; break;
                // T: Cut the wire if the first LED is on.
                case 'T': _wires[i].CutRule = _ledsOn[0] ? CutRule.Cut : CutRule.DontCut; break;
                // U: Cut the wire if its arrow points up or down.
                case 'U': _wires[i].CutRule = dir == Arrow.Up || dir == Arrow.Down ? CutRule.Cut : CutRule.DontCut; break;
                // M: Cut the wire if the arrow points down or right.
                case 'M': _wires[i].CutRule = dir == Arrow.Right || dir == Arrow.Down ? CutRule.Cut : CutRule.DontCut; break;
                // H: Cut the wire if the wire shares a star with another wire.
                case 'H': _wires[i].CutRule = _wires.Where((w, ix) => ix != i).Any(w => w.TopConnector == _wires[i].TopConnector) ? CutRule.Cut : CutRule.DontCut; break;
                // P: Cut the wire if its position at the bottom is equal to the number of ports.
                case 'P': _wires[i].CutRule = _wires[i].BottomConnector + 1 == Bomb.GetPortCount() ? CutRule.Cut : CutRule.DontCut; break;
                // B: Cut the wire if its position at the bottom is equal to the number of batteries.
                case 'B': _wires[i].CutRule = _wires[i].BottomConnector + 1 == Bomb.GetBatteryCount() ? CutRule.Cut : CutRule.DontCut; break;
                // I: Cut the wire if its position at the bottom is equal to the number of indicators.
                case 'I': _wires[i].CutRule = _wires[i].BottomConnector + 1 == Bomb.GetIndicators().Count() ? CutRule.Cut : CutRule.DontCut; break;
                // Q: Cut the wire if the color of the wire is unique.
                case 'Q': _wires[i].CutRule = _wires.Where((w, ix) => ix != i).Any(w => w.Color == _wires[i].Color) ? CutRule.DontCut : CutRule.Cut; break;
                // J: Cut the wire if, at the bottom, it is adjacent to an orange or purple wire.
                case 'J': _wires[i].CutRule = _wires.Any(w => (w.BottomConnector == _wires[i].BottomConnector - 1 || w.BottomConnector == _wires[i].BottomConnector + 1) && (w.Color == WireColor.Orange || w.Color == WireColor.Purple)) ? CutRule.Cut : CutRule.DontCut; break;
                // V: Cut the wire if the serial number has a vowel, or if the bomb has a USB port.
                case 'V': _wires[i].CutRule = Bomb.GetSerialNumberLetters().Any(ch => "AEIOU".Contains(ch)) || Bomb.GetPortCount("USB") > 0 ? CutRule.Cut : CutRule.DontCut; break;
                // R: Cut the wire if its arrow direction is unique.
                case 'R': _wires[i].CutRule = _arrows.Where((a, ix) => ix != _wires[i].BottomConnector).Any(a => (a & Arrow.DirectionMask) == dir) ? CutRule.DontCut : CutRule.Cut; break;
                // D: Do not cut the wire.
                case 'D': _wires[i].CutRule = CutRule.DontCut; break;
            }
        }
        if (_wires.All(w => w.CutRule == CutRule.DontCut))
        {
            retries++;
            goto retry;
        }
        Array.Sort(_wires, (w1, w2) => w1.BottomConnector.CompareTo(w2.BottomConnector));

        for (int i = 0; i < StarMeshes.Length; i++)
            Debug.LogFormat("[Perplexing Wires #{0}] Star #{1} is {2}.", _moduleId, i + 1, _filledStars[i] ? "filled" : "empty");
        for (int i = 0; i < ArrowMeshes.Length; i++)
            Debug.LogFormat("[Perplexing Wires #{0}] Arrow #{1} is {2} and pointing {3}.", _moduleId, i + 1, arrowColorStr(_arrows[i]), arrowDirStr(_arrows[i]));
        for (int i = 0; i < LedMeshes.Length; i++)
            Debug.LogFormat("[Perplexing Wires #{0}] LED #{1} is {2}.", _moduleId, i + 1, _ledsOn[i] ? "on" : "off");

        //
        // STEP 4: Generate the actual wire meshes. (This also logs the wire states and rules.)
        //
        var wiresParent = Module.transform.FindChild("Wires");
        for (int wIx = 0; wIx < _wires.Length; wIx++)
        {
            var wireObj = wiresParent.FindChild("Wire" + (wIx + 1)).gameObject;

            // Determine the “original” control points and raise height.
            var topConnector = Module.transform.FindChild("Strip2").FindChild("Connector" + (_wires[wIx].TopConnector + 1));
            var topControl = topConnector.FindChild("Control");
            topControl.localPosition = new Vector3(0, .2f, 0);
            var bottomConnector = Module.transform.FindChild("Strip1").FindChild("Connector" + (_wires[wIx].BottomConnector + 1));
            var bottomControl = bottomConnector.FindChild("Control");
            var raiseBy = 1.5 * (_wires[wIx].Level - 1) * (Pt) (transform.InverseTransformPoint(bottomControl.position) - transform.InverseTransformPoint(bottomConnector.position) + transform.InverseTransformPoint(topControl.position) - transform.InverseTransformPoint(topConnector.position));

            // Slightly move the control point at the top connector to mitigate the incidence of wire collisions.
            var topControlX = 0f;
            if (_wires.Any(w => w.TopConnector == _wires[wIx].TopConnector && w.BottomConnector < _wires[wIx].BottomConnector) && !_wires.Any(w => w.TopConnector == _wires[wIx].TopConnector && w.BottomConnector > _wires[wIx].BottomConnector))
                topControlX = -.07f;
            else if (_wires.Any(w => w.TopConnector == _wires[wIx].TopConnector && w.BottomConnector > _wires[wIx].BottomConnector) && !_wires.Any(w => w.TopConnector == _wires[wIx].TopConnector && w.BottomConnector < _wires[wIx].BottomConnector))
                topControlX = .07f;
            var topControlZ = 0f;
            if (_wires.Any(w => Math.Sign(w.TopConnector - _wires[wIx].TopConnector) != Math.Sign(w.BottomConnector - _wires[wIx].BottomConnector) && w.Level < _wires[wIx].Level))
                topControlZ = .05f;
            else if (_wires.Any(w => Math.Sign(w.TopConnector - _wires[wIx].TopConnector) != Math.Sign(w.BottomConnector - _wires[wIx].BottomConnector) && w.Level > _wires[wIx].Level))
                topControlZ = -.05f;
            topControl.localPosition = new Vector3(topControlX, .2f, topControlZ);

            // Generate the meshes for this wire.
            var seed = Rnd.Range(0, int.MaxValue);
            var mesh = MeshGenerator.GenerateWire(
                transform.InverseTransformPoint(topConnector.position),
                transform.InverseTransformPoint(topControl.position),
                transform.InverseTransformPoint(bottomControl.position),
                transform.InverseTransformPoint(bottomConnector.position),
                5,
                MeshGenerator.WirePiece.Uncut,
                MeshGenerator.Mode.Wire,
                seed,
                raiseBy);
            _wires[wIx].MeshFilter = wireObj.GetComponent<MeshFilter>();
            _wires[wIx].MeshFilter.mesh = mesh;
            wireObj.GetComponent<MeshRenderer>().material = WireMaterials[(int) _wires[wIx].Color];

            _wires[wIx].CutMesh = MeshGenerator.GenerateWire(
                transform.InverseTransformPoint(topConnector.position),
                transform.InverseTransformPoint(topControl.position),
                transform.InverseTransformPoint(bottomControl.position),
                transform.InverseTransformPoint(bottomConnector.position),
                5,
                MeshGenerator.WirePiece.Cut,
                MeshGenerator.Mode.Wire,
                seed,
                raiseBy);
            _wires[wIx].CutHighlightMesh = MeshGenerator.GenerateWire(
                transform.InverseTransformPoint(topConnector.position),
                transform.InverseTransformPoint(topControl.position),
                transform.InverseTransformPoint(bottomControl.position),
                transform.InverseTransformPoint(bottomConnector.position),
                5,
                MeshGenerator.WirePiece.Cut,
                MeshGenerator.Mode.Highlight,
                seed,
                raiseBy);
            _wires[wIx].CopperMesh = MeshGenerator.GenerateWire(
                transform.InverseTransformPoint(topConnector.position),
                transform.InverseTransformPoint(topControl.position),
                transform.InverseTransformPoint(bottomControl.position),
                transform.InverseTransformPoint(bottomConnector.position),
                5,
                MeshGenerator.WirePiece.Copper,
                MeshGenerator.Mode.Wire,
                seed,
                raiseBy);

            var highlightMesh = MeshGenerator.GenerateWire(
                transform.InverseTransformPoint(topConnector.position),
                transform.InverseTransformPoint(topControl.position),
                transform.InverseTransformPoint(bottomControl.position),
                transform.InverseTransformPoint(bottomConnector.position),
                5,
                MeshGenerator.WirePiece.Uncut,
                MeshGenerator.Mode.Highlight,
                seed,
                raiseBy);
            var highlight = wireObj.transform.FindChild("Highlight");
            _wires[wIx].HighlightMeshFilter = highlight.GetComponent<MeshFilter>();
            _wires[wIx].HighlightMeshFilter.mesh = highlightMesh;
            var highlight2 = highlight.FindChild("Highlight(Clone)");
            if (highlight2 != null)
            {
                _wires[wIx].HighlightMeshFilter = highlight2.GetComponent<MeshFilter>();
                _wires[wIx].HighlightMeshFilter.mesh = highlightMesh;
            }

            wireObj.GetComponent<MeshCollider>().sharedMesh = MeshGenerator.GenerateWire(
                transform.InverseTransformPoint(topConnector.position),
                transform.InverseTransformPoint(topControl.position),
                transform.InverseTransformPoint(bottomControl.position),
                transform.InverseTransformPoint(bottomConnector.position),
                5,
                MeshGenerator.WirePiece.Uncut,
                MeshGenerator.Mode.Collider,
                seed,
                raiseBy);

            // Fix for a possible bug in Unity
            wireObj.GetComponent<MeshCollider>().enabled = true;
            wireObj.GetComponent<MeshCollider>().enabled = false;

            Debug.LogFormat("[Perplexing Wires #{6}] Wire {0} to {1} is {2}: {3} (Venn: {4})", _wires[wIx].TopConnector + 1, _wires[wIx].BottomConnector + 1, _wires[wIx].Color, cutRuleStr(_wires[wIx].CutRule), _wires[wIx].Reason, _wires[wIx].Level, _moduleId);
            _wires[wIx].Selectable = wireObj.GetComponent<KMSelectable>();
            _wires[wIx].Selectable.OnInteract = getWireHandler(wIx);
        }

        // Finally, get rid of the extra Wire objects that only exist to hold the colored materials.
        var dwIx = _wires.Length;
        while (true)
        {
            var wireObj = wiresParent.FindChild("Wire" + (dwIx + 1));
            if (wireObj == null)
                break;
            Destroy(wireObj.gameObject);
            dwIx++;
        }

        // Object that will be cloned to create the wire copper when a wire is cut.
        _wireCopper = wiresParent.FindChild("WireCopper").gameObject;
        _wireCopper.SetActive(false);

        MainSelectable.Children = _wires.Select(w => w.Selectable).ToArray();
        MainSelectable.UpdateChildren();
    }

    private KMSelectable.OnInteractHandler getWireHandler(int ix)
    {
        return delegate
        {
            if (_wires[ix].HasBeenCut)
                return false;
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.WireSnip, _wires[ix].MeshFilter.transform);
            _wires[ix].Selectable.AddInteractionPunch(.2f);

            _wires[ix].MeshFilter.mesh = _wires[ix].CutMesh;
            _wires[ix].HighlightMeshFilter.mesh = _wires[ix].CutHighlightMesh;

            var copper = Instantiate(_wireCopper);
            copper.SetActive(true);
            copper.transform.parent = _wires[ix].MeshFilter.transform;
            copper.transform.localPosition = new Vector3(0, 0, 0);
            copper.transform.localEulerAngles = new Vector3(0, 0, 0);
            copper.transform.localScale = new Vector3(1, 1, 1);
            copper.GetComponent<MeshFilter>().mesh = _wires[ix].CopperMesh;

            var correct = true;
            if (_wires[ix].CutRule == CutRule.DontCut ||
                (_wires[ix].CutRule == CutRule.Cut && _wires.Any(w => w.CutRule == CutRule.CutFirst && !w.HasBeenCut)) ||
                (_wires[ix].CutRule == CutRule.CutLast && _wires.Any(w => (w.CutRule == CutRule.Cut || w.CutRule == CutRule.CutFirst) && !w.HasBeenCut)))
            {
                Module.HandleStrike();
                correct = false;
            }
            Debug.LogFormat("[Perplexing Wires #{0}] Cutting wire {1} to {2} was {3}", _moduleId, _wires[ix].TopConnector + 1, _wires[ix].BottomConnector + 1, correct ? "correct." : "wrong. Strike.");

            _wires[ix].HasBeenCut = true;
            if (_wires.All(w => w.CutRule == CutRule.DontCut || w.HasBeenCut))
            {
                Debug.LogFormat("[Perplexing Wires #{0}] Module solved.", _moduleId);
                Module.HandlePass();
            }
            return false;
        };
    }

    private string TwitchHelpMessage = "Cut the wires with !{0} cut 2 3 1.";
    private KMSelectable[] ProcessTwitchCommand(string command)
    {
        var split = command.ToLowerInvariant().Split(new[] {" "}, StringSplitOptions.RemoveEmptyEntries);
        if (split.Length < 2 || split[0] != "cut")
            return null;
        var wires = new List<KMSelectable>();
        foreach (var wire in split.Skip(1))
        {
            int result;
            if (!int.TryParse(wire, out result) || result < 1 || result > 6)
                return null;
            wires.Add(_wires[result-1].Selectable);
        }
        return wires.ToArray();
    }
}
