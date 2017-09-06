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

    public Texture[] Arrows;
    public Texture[] Stars;
    public Material[] LedMaterials;

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

    [Flags] enum Color { Red, Yellow, Blue, White, Green, Orange, Purple, Black }

    private Arrow[] _arrows;
    private bool[] _filledStars;
    private bool[] _ledsOn;

    enum CutRule { DontCut, CutFirst, CutLast, Cut }

    sealed class WireInfo
    {
        public int TopConnector;
        public int BottomConnector;
        public Color Color;
        public int Level;
        public CutRule MustBeCut;
        public bool HasBeenCut;
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
        yield return new WaitForSeconds(Rnd.Range(.1f, .5f));

        retry:

        //
        // STEP 1: Decide on all the arrows, filled stars and LED states
        //
        _arrows = new Arrow[ArrowMeshes.Length];
        for (int i = 0; i < ArrowMeshes.Length; i++)
        {
            _arrows[i] = (Arrow) Rnd.Range(0, (3 << 0) + (4 << 2) + 1);
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
                Color = (Color) Rnd.Range(0, 8),
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
        var colorsForRedRule = new[] { Color.Red, Color.Yellow, Color.Blue, Color.White };
        for (int i = 0; i < 6; i++)
        {
            var rule = 0;
            // Blue: The wire crosses over another wire.
            if (_wires.Any(w => (w.BottomConnector > _wires[i].BottomConnector && w.TopConnector < _wires[i].TopConnector) || (w.BottomConnector < _wires[i].BottomConnector && w.TopConnector > _wires[i].TopConnector)))
                rule += 1;
            // Yellow: The wire’s star is black.
            if (_filledStars[_wires[i].TopConnector])
                rule += 2;
            // Green: The wire’s position on the bottom is even.
            if (_wires[i].BottomConnector % 2 != 0)
                rule += 4;
            // Red: The wire is red, yellow, blue, or white.
            if (colorsForRedRule.Contains(_wires[i].Color))
                rule += 8;
            // Orange: The wire shares the same color as its arrow.
            if ((_wires[i].Color == Color.Red && (_arrows[_wires[i].BottomConnector] & Arrow.ColorMask) == Arrow.Red) ||
                (_wires[i].Color == Color.Green && (_arrows[_wires[i].BottomConnector] & Arrow.ColorMask) == Arrow.Green) ||
                (_wires[i].Color == Color.Blue && (_arrows[_wires[i].BottomConnector] & Arrow.ColorMask) == Arrow.Blue) ||
                (_wires[i].Color == Color.Yellow && (_arrows[_wires[i].BottomConnector] & Arrow.ColorMask) == Arrow.Yellow) ||
                (_wires[i].Color == Color.Purple && (_arrows[_wires[i].BottomConnector] & Arrow.ColorMask) == Arrow.Purple))
                rule += 16;

            var dir = (_arrows[_wires[i].BottomConnector] & Arrow.DirectionMask);
            switch (rules[rule])
            {
                // C: Cut the wire.
                case 'C': _wires[i].MustBeCut = CutRule.Cut; break;
                // F: Always cut the wire, but only cut it first.
                case 'F': _wires[i].MustBeCut = CutRule.CutFirst; break;
                // L: Always cut the wire, but only cut it last.
                case 'L': _wires[i].MustBeCut = CutRule.CutLast; break;
                // W: Cut the wire if more of the LEDs are on than off.
                case 'W': _wires[i].MustBeCut = _ledsOn.Count(l => l) > 1 ? CutRule.Cut : CutRule.DontCut; break;
                // T: Cut the wire if the first LED is on.
                case 'T': _wires[i].MustBeCut = _ledsOn[0] ? CutRule.Cut : CutRule.DontCut; break;
                // U: Cut the wire if its arrow points up or down.
                case 'U': _wires[i].MustBeCut = dir == Arrow.Up || dir == Arrow.Down ? CutRule.Cut : CutRule.DontCut; break;
                // M: Cut the wire if the arrow points down or right.
                case 'M': _wires[i].MustBeCut = dir == Arrow.Right || dir == Arrow.Down ? CutRule.Cut : CutRule.DontCut; break;
                // H: Cut the wire if the wire shares a star with another wire.
                case 'H': _wires[i].MustBeCut = _wires.Where((w, ix) => ix != i).Any(w => w.TopConnector == _wires[i].TopConnector) ? CutRule.Cut : CutRule.DontCut; break;
                // P: Cut the wire if its position at the bottom is equal to the number of ports.
                case 'P': _wires[i].MustBeCut = _wires[i].BottomConnector + 1 == Bomb.GetPortCount() ? CutRule.Cut : CutRule.DontCut; break;
                // B: Cut the wire if its position at the bottom is equal to the number of batteries.
                case 'B': _wires[i].MustBeCut = _wires[i].BottomConnector + 1 == Bomb.GetBatteryCount() ? CutRule.Cut : CutRule.DontCut; break;
                // I: Cut the wire if its position at the bottom is equal to the number of indicators.
                case 'I': _wires[i].MustBeCut = _wires[i].BottomConnector + 1 == Bomb.GetIndicators().Count() ? CutRule.Cut : CutRule.DontCut; break;
                // Q: Cut the wire if the color of the wire is unique.
                case 'Q': _wires[i].MustBeCut = _wires.Where((w, ix) => ix != i).Any(w => w.Color == _wires[i].Color) ? CutRule.DontCut : CutRule.Cut; break;
                // J: Cut the wire if, at the bottom, it is adjacent to an orange or purple wire.
                case 'J': _wires[i].MustBeCut = _wires.Any(w => (w.BottomConnector == _wires[i].BottomConnector - 1 || w.BottomConnector == _wires[i].BottomConnector + 1) && (w.Color == Color.Orange || w.Color == Color.Purple)) ? CutRule.Cut : CutRule.DontCut; break;
                // V: Cut the wire if the serial number has a vowel, or if the bomb has a USB port.
                case 'V': _wires[i].MustBeCut = Bomb.GetSerialNumberLetters().Any(ch => "AEIOU".Contains(ch)) || Bomb.GetPortCount("USB") > 0 ? CutRule.Cut : CutRule.DontCut; break;
                // R: Cut the wire if its arrow direction is unique.
                case 'R': _wires[i].MustBeCut = _arrows.Where((a, ix) => ix != _wires[i].BottomConnector).Any(a => (a & Arrow.DirectionMask) == dir) ? CutRule.DontCut : CutRule.Cut; break;
                // D: Do not cut the wire.
                case 'D': _wires[i].MustBeCut = CutRule.DontCut; break;
            }
        }
        if (_wires.All(w => w.MustBeCut == CutRule.DontCut))
            goto retry;

        //
        // STEP 4: Generate the actual wire meshes.
        //
        var wireOrig = Module.transform.FindChild("Wire").gameObject;
        foreach (var wire in _wires)
        {
            var topConnector = Module.transform.FindChild("Strip2").FindChild("Connector" + (wire.TopConnector + 1));
            var bottomConnector = Module.transform.FindChild("Strip1").FindChild("Connector" + (wire.BottomConnector + 1));
            var seed = Rnd.Range(0, int.MaxValue);
            var mesh = MeshGenerator.GenerateWire(
                topConnector.position,
                topConnector.FindChild("Control").position,
                bottomConnector.FindChild("Control").position,
                bottomConnector.position,
                3,
                MeshGenerator.WirePiece.Uncut,
                highlight: false,
                seed: seed,
                raiseFactor: .75 * wire.Level);
            var wireObj = Instantiate(wireOrig);
            wireObj.GetComponent<MeshFilter>().mesh = mesh;
            wireObj.transform.parent = Module.transform;

            var highlightMesh = MeshGenerator.GenerateWire(
                topConnector.position,
                topConnector.FindChild("Control").position,
                bottomConnector.FindChild("Control").position,
                bottomConnector.position,
                3,
                MeshGenerator.WirePiece.Uncut,
                highlight: true,
                seed: seed,
                raiseFactor: .75 * (wire.Level - 1));
            var highlight = wireObj.transform.FindChild("Highlight");
            //highlight.GetComponent<MeshFilter>().mesh = highlightMesh;

            Debug.LogFormat("Wire ({0} to {1}) Level {3}: {2}", wire.TopConnector, wire.BottomConnector, wire.MustBeCut, wire.Level);
        }
        Destroy(wireOrig);
    }
}
