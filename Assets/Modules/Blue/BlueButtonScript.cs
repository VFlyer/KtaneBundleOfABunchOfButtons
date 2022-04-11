using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BlueButtonLib;
using UnityEngine;

using Rnd = UnityEngine.Random;

public class BlueButtonScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMAudio Audio;
    public KMBombInfo BombInfo;
    public KMSelectable BlueButtonSelectable;
    public GameObject BlueButtonCap;

    public Transform PolyominoesParent;
    public Transform ColorsParent;
    public Transform EquationsParent;
    public Transform SuitsParent;
    public Transform WordsParent;
    public Transform ResetParent;

    // Objects for instantiating/animating
    public MeshRenderer Mask;
    public Color[] DiffuseColors;
    public Mesh PolyominoCubelet;
    public Mesh ColorBlob;
    public Light ColorsSpotlight;
    public TextMesh EquationTemplate;
    public TextMesh ResetText;
    public MeshFilter[] SuitObjects;
    public MeshRenderer[] SuitRenderers;
    public MeshRenderer[] SuitSeparators;
    public Mesh[] SuitMeshes;
    public Material SuitMasked;
    public Material SuitUnmasked;
    public Material SuitHighlighted;
    public TextMesh[] WordTexts;
    public TextMesh WordResultText;
    public MaskShaderManager MaskShaderManager;

    // Puzzle
    private PolyominoPlacement[] _polyominoSequence;
    private int[] _polyominoSequenceColors;
    private int[] _colorStageColors;
    private int[] _equationOffsets;
    private string[] _equations;
    private int[] _suitsCurrent;
    private int[] _suitsGoal;
    private int[] _jumps;
    private string _word;

    // Solving process
    private Stage _stage;
    private int _colorHighlight;
    private int _eqTaps;
    private Coroutine _eqTapTimeout;
    private readonly float[] _suitTapTimes = new float[4];
    private int _suitTapIx;
    private int _wordHighlight;
    private int _wordSection;
    private int _wordProgress;

    // Internals
    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private Coroutine _pressHandler;
    private MaskMaterials _maskMaterials;
    private int[] _tpOverrideRAB;
    private bool _tpOverriddenRAB;

    enum Stage
    {
        Polyominoes,
        Colors,
        Equations,
        Suits,
        Word,
        Reset,
        Solved
    }

    public static readonly string[] _colorNames = { "Blue", "Green", "Cyan", "Red", "Magenta", "Yellow" };

    private void Start()
    {
        _moduleId = _moduleIdCounter++;
        BlueButtonSelectable.OnInteract += BlueButtonPress;
        BlueButtonSelectable.OnInteractEnded += BlueButtonRelease;
        ColorsSpotlight.range *= transform.lossyScale.x;
        _maskMaterials = MaskShaderManager.MakeMaterials();
        _maskMaterials.Text.mainTexture = WordResultText.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        _maskMaterials.DiffuseText.mainTexture = WordResultText.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        Mask.sharedMaterial = _maskMaterials.Mask;

        GeneratePuzzle();
        _stage = Stage.Polyominoes;

        StartCoroutine(AnimationManager(Stage.Polyominoes, PolyominoesParent, AnimatePolyominoSequence));
        StartCoroutine(AnimationManager(Stage.Colors, ColorsParent, AnimateColorSequence, ColorsSpotlight));
        StartCoroutine(AnimationManager(Stage.Equations, EquationsParent, AnimateEquations));
        StartCoroutine(AnimationManager(Stage.Suits, SuitsParent, AnimateSuits));
        StartCoroutine(AnimationManager(new[] { Stage.Word, Stage.Solved }, WordsParent, AnimateWordsAndSolve));
        StartCoroutine(AnimationManager(Stage.Reset, ResetParent, AnimateReset));
    }

    private void OnDestroy()
    {
        MaskShaderManager.Clear();
    }

    private bool BlueButtonPress()
    {
        StartCoroutine(AnimateButton(0f, -0.05f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
        if (_eqTapTimeout != null)
            StopCoroutine(_eqTapTimeout);
        if (_stage != Stage.Solved)
            _pressHandler = StartCoroutine(HandlePress());
        return false;
    }

    private void BlueButtonRelease()
    {
        StartCoroutine(AnimateButton(-0.05f, 0f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
        if (_pressHandler != null)
            StopCoroutine(_pressHandler);

        switch (_stage)
        {
            case Stage.Polyominoes:
                _stage = Stage.Colors;
                break;

            case Stage.Colors:
                if (_colorHighlight != 3)
                {
                    Debug.LogFormat(@"[The Blue Button #{0}] Stage 2: You submitted {1} (position {2}). Strike!", _moduleId, _colorNames[_colorStageColors[_colorHighlight]], _colorHighlight + 1);
                    Module.HandleStrike();
                }
                else
                {
                    _stage = Stage.Equations;
                    _eqTaps = 0;
                }
                break;

            case Stage.Equations:
                if (_eqTapTimeout != null)
                    StopCoroutine(_eqTapTimeout);
                _eqTaps++;
                _eqTapTimeout = StartCoroutine(EquationTimeout());
                break;

            case Stage.Suits:
                _suitTapTimes[_suitTapIx] = Time.time;
                if (_suitTapIx > 0)
                    Debug.LogFormat(@"<The Blue Button #{0}> Interval {1} = {2}", _moduleId, "RAB"[_suitTapIx - 1], _suitTapTimes[_suitTapIx] - _suitTapTimes[_suitTapIx - 1]);
                _suitTapIx++;
                if (_suitTapIx == 4)
                {
                    interpretSuitInput();
                    _suitTapIx = 0;
                }
                break;

            case Stage.Word:
                if (_wordSection == 0)
                {
                    if (_wordHighlight != (_word[_wordProgress] - 'A') / 9)
                    {
                        Debug.LogFormat(@"[The Blue Button #{0}] Stage 5: You selected section {1} for letter #{2}. Strike!", _moduleId, WordTexts[_wordHighlight].text, _wordProgress + 1);
                        Module.HandleStrike();
                    }
                    else
                        _wordSection = _wordHighlight + 1;
                }
                else if (_wordSection <= 3)
                {
                    if (_wordHighlight != ((_word[_wordProgress] - 'A') % 9) / 3)
                    {
                        Debug.LogFormat(@"[The Blue Button #{0}] Stage 5: You selected section {1} for letter #{2}. Strike!", _moduleId, WordTexts[_wordHighlight].text, _wordProgress + 1);
                        Module.HandleStrike();
                        _wordSection = 0;
                    }
                    else
                        _wordSection = (_wordSection - 1) * 3 + _wordHighlight + 4;
                }
                else if (_wordSection + _wordHighlight >= 30)
                {
                    Debug.LogFormat(@"[The Blue Button #{0}] Stage 5: You submitted the empty slot after Z. Strike!", _moduleId);
                    Module.HandleStrike();
                    _wordSection = 0;
                }
                else
                {
                    var nextLetter = (char) ('A' + ((_wordSection - 4) * 3 + _wordHighlight));
                    if (nextLetter != _word[_wordProgress])
                    {
                        Debug.LogFormat(@"[The Blue Button #{0}] Stage 5: You submitted {1} for letter #{2}. Strike!", _moduleId, nextLetter, _wordProgress + 1);
                        Module.HandleStrike();
                    }
                    else
                    {
                        _wordProgress++;
                        if (_wordProgress == _word.Length)
                        {
                            Module.HandlePass();
                            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
                            _stage = Stage.Solved;
                        }
                    }
                    _wordSection = 0;
                }
                break;

            case Stage.Reset:
                _stage = Stage.Polyominoes;
                _wordProgress = 0;
                break;
        }
    }

    private void interpretSuitInput()
    {
        var gap1 = _suitTapTimes[1] - _suitTapTimes[0];
        var gap2 = _suitTapTimes[2] - _suitTapTimes[1];
        var gap3 = _suitTapTimes[3] - _suitTapTimes[2];

        var aGtR = gap2 > gap1;
        var bGtR = gap3 > gap1;

        if (_tpOverrideRAB != null)
        {
            var oAGtR = _tpOverrideRAB[1] > _tpOverrideRAB[0];
            var oBGtR = _tpOverrideRAB[2] > _tpOverrideRAB[0];
            if (oAGtR != aGtR || oBGtR != bGtR)
            {
                _tpOverriddenRAB = true;
                aGtR = oAGtR;
                bGtR = oBGtR;
            }
        }

        if (aGtR && bGtR)
        {
            // Submit
            if (_suitsCurrent.SequenceEqual(_suitsGoal))
            {
                _stage = Stage.Word;
            }
            else
            {
                Debug.LogFormat(@"[The Blue Button #{0}] Stage 4: You submitted {1}. Strike!", _moduleId, _suitsCurrent.Select(suit => "♠♥♣♦"[suit]).Join(""));
                Module.HandleStrike();
            }
            return;
        }

        // Which positions to swap: { swap, swap + 1 }
        var swap = bGtR ? 2 : aGtR ? 1 : 0;
        var t = _suitsCurrent[swap];
        _suitsCurrent[swap] = _suitsCurrent[swap + 1];
        _suitsCurrent[swap + 1] = t;
    }

    private IEnumerator HandlePress()
    {
        yield return new WaitForSeconds(.5f);
        Audio.PlaySoundAtTransform("BlueButtonSwoosh", transform);
        _stage = Stage.Reset;
    }

    private IEnumerator AnimationManager(Stage requiredStage, Transform parent, Func<Func<bool>, IEnumerator> gen, Light spotlight = null)
    {
        return AnimationManager(new[] { requiredStage }, parent, gen, spotlight);
    }

    private IEnumerator AnimationManager(Stage[] requiredStages, Transform parent, Func<Func<bool>, IEnumerator> gen, Light spotlight = null)
    {
        while (true)
        {
            parent.gameObject.SetActive(false);
            if (spotlight != null)
                spotlight.gameObject.SetActive(false);

            while (!requiredStages.Contains(_stage))
            {
                if (_stage == Stage.Solved)
                    yield break;
                yield return null;
            }

            var stop = false;
            StartCoroutine(gen(() => stop));
            yield return new WaitForSeconds(1f);
            parent.localPosition = new Vector3(0, 0, -.2f);
            parent.gameObject.SetActive(true);
            if (spotlight != null)
            {
                spotlight.intensity = 0;
                spotlight.gameObject.SetActive(true);
            }
            yield return Animation(.63f, t =>
            {
                parent.localPosition = new Vector3(0, 0, Easing.BackOut(t, -.2f, 0, 1));
                if (spotlight != null)
                    spotlight.intensity = 5 * t;
            });

            while (requiredStages.Contains(_stage))
            {
                if (_stage == Stage.Solved)
                    yield break;
                yield return null;
            }

            yield return Animation(.63f, t =>
            {
                parent.localPosition = new Vector3(0, 0, Easing.BackIn(t, 0, .2f, 1));
                if (spotlight != null)
                    spotlight.intensity = 5 * (1 - t);
            });

            stop = true;
        }
    }

    private IEnumerator Animation(float duration, Action<float> action)
    {
        var elapsed = 0f;
        while (elapsed < duration)
        {
            action(elapsed / duration);
            yield return null;
            elapsed += Time.deltaTime;
        }
        action(1);
    }

    private IEnumerator AnimatePolyominoSequence(Func<bool> stop)
    {
        var scroller = MakeGameObject("Polyominoes scroller", PolyominoesParent, scale: .025f);
        var width = 0f;
        var numCopies = 0;

        while (width < 20 || numCopies < 2)
        {
            for (int i = 0; i < _polyominoSequence.Length; i++)
            {
                var polyParent = MakeGameObject(string.Format("Polyomino #{0}", i + 1), scroller.transform);

                var h = 0;
                foreach (var block in _polyominoSequence[i].Polyomino.Cells)
                {
                    h = Math.Max(block.Y, h);
                    var blockObj = MakeGameObject(string.Format("Block {0}", block), polyParent.transform, position: new Vector3(block.X, 0, -block.Y));
                    blockObj.AddComponent<MeshFilter>().sharedMesh = PolyominoCubelet;
                    var mr = blockObj.AddComponent<MeshRenderer>();
                    mr.material = _maskMaterials.DiffuseTint;
                    mr.material.color = DiffuseColors[_polyominoSequenceColors[i]];
                }

                polyParent.transform.localPosition = new Vector3(width, 0, h * .5f);
                width += _polyominoSequence[i].Polyomino.Cells.Max(cell => cell.X) + 2.5f;
            }
            numCopies++;
        }
        width /= numCopies;

        while (!stop())
        {
            scroller.transform.localPosition = new Vector3(-((2f * Time.time) % width) * .025f - 0.15f, -0.025f, 0);
            yield return null;
        }

        Destroy(scroller);
    }

    private IEnumerator AnimateColorSequence(Func<bool> stop)
    {
        var scroller = MakeGameObject("Colors scroller", ColorsParent);
        var width = 0f;
        var numCopies = 0;
        const float separation = .125f;
        const float spotlightDistance = 1f / 208 * 190;

        while (numCopies < 2)
        {
            for (int i = 0; i < _colorStageColors.Length; i++)
            {
                var blobObj = MakeGameObject(string.Format("Color {0}", i), scroller.transform, position: new Vector3(width, 0, 0), scale: .04f);
                blobObj.AddComponent<MeshFilter>().sharedMesh = ColorBlob;
                var mr = blobObj.AddComponent<MeshRenderer>();
                mr.material = _maskMaterials.DiffuseTint;
                mr.material.color = DiffuseColors[_colorStageColors[i]];
                width += separation;
            }
            numCopies++;
        }
        width /= numCopies;

        while (!stop())
        {
            scroller.transform.localPosition = new Vector3(-((.1f * Time.time) % width) - 0.15f, -.025f, 0);

            var pos = (((.1f * Time.time) % width) + 0.15f) / separation;
            var selected = Mathf.RoundToInt(pos);

            /*
                restart;
                v1 := t -> -a*t+C1;
                v2 := t -> a*t+C2;
                v3 := unapply(diff(arctan(t/s)*180/3.1415926535897932384626433832795+180, t), t);
                d1 := t -> -1/2*a*t^2 + C1*t + C4;
                d2 := t -> 1/2*a*t^2 + C2*t + C5;
                d3 := t -> arctan(t/s)*180/3.1415926535897932384626433832795+180;
                q := -.4;
                r := -.3;
                s := 1.0 / 208 * 190;
                solve({
                  v1(-1/2) = v3(1/2),
                  v1(q) = v2(q),
                  d1(-1/2) = d3(1/2),
                  d1(q) = d2(q),
                  d2(r) = d3(r)
                }, { C1, C2, C4, C5, a });
            */
            var t = pos - selected;
            const float q = -.4f, r = -.3f, C2 = 1744.129529f, a = 5652.886846f, C5 = 430.6776816f, C1 = -2778.179948f, C4 = -473.7842137f;
            var calcAngle =
                t < q ? -.5f * a * Mathf.Pow(t, 2) + C1 * t + C4 :
                t < r ? .5f * a * Mathf.Pow(t, 2) + C2 * t + C5 :
                180 + Mathf.Atan2(t, spotlightDistance) * 180 / Mathf.PI;

            ColorsSpotlight.transform.localEulerAngles = new Vector3(40, calcAngle, 0);
            _colorHighlight = selected % _colorStageColors.Length;
            yield return null;
        }

        Destroy(scroller);
    }

    private IEnumerator AnimateEquations(Func<bool> stop)
    {
        var scroller = MakeGameObject("Equations scroller", EquationsParent, scale: .025f);
        var width = 0f;
        var numCopies = 0;

        while (numCopies < 2)
        {
            for (int i = 0; i < _equations.Length; i++)
            {
                var equation = Instantiate(EquationTemplate, scroller.transform);
                equation.GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;
                equation.name = string.Format("Equation #{0}", i + 1);
                equation.transform.localPosition = new Vector3(width, 0, 0);
                equation.transform.localEulerAngles = new Vector3(90, 0, 0);
                equation.transform.localScale = new Vector3(1, 1, 1);
                equation.gameObject.SetActive(true);
                equation.text = _equations[i];
                width += 20f;
            }
            numCopies++;
        }
        width /= numCopies;

        while (!stop())
        {
            scroller.transform.localPosition = new Vector3(-((6f * Time.time) % width) * .025f - 0.15f, -0.025f, 0);
            yield return null;
        }

        Destroy(scroller);
    }

    private IEnumerator AnimateSuits(Func<bool> stop)
    {
        var suitsPrev = _suitsCurrent.ToArray();
        SetSuitMeshes(suitsPrev);
        while (true)
        {
            while (!stop() && suitsPrev.SequenceEqual(_suitsCurrent))
                yield return null;
            if (stop())
                break;
            var suitsNew = _suitsCurrent.ToArray();

            var randomRotations = suitsPrev.Select(_ => Quaternion.Euler(Rnd.Range(0f, 360f), Rnd.Range(0f, 360f), Rnd.Range(0f, 360f))).ToArray();
            var newPositions = suitsPrev.Select(suit => Array.IndexOf(suitsNew, suit)).ToArray();
            for (var rIx = 0; rIx < SuitRenderers.Length; rIx++)
                SuitRenderers[rIx].sharedMaterial = rIx == newPositions[rIx] ? SuitUnmasked : SuitHighlighted;
            SuitSeparators[Enumerable.Range(0, 4).First(i => i != newPositions[i])].sharedMaterial = SuitHighlighted;

            yield return Animation(1.3f, t =>
            {
                for (var i = 0; i < 4; i++)
                    if (newPositions[i] != i)
                    {
                        SuitObjects[i].transform.localPosition = new Vector3(
                            Easing.InOutQuart(t, SuitX(i), SuitX(newPositions[i]), 1),
                            .3f * t * (t - 1) * (t - 1) - 0.01f,
                            .3f * t * (t - 1) * (t - 1) * Mathf.Sign(i - newPositions[i]));
                        SuitObjects[i].transform.localRotation = Quaternion.Slerp(Quaternion.identity, randomRotations[i], t < .5 ? Easing.OutQuad(t, 0, 1, .5f) : Easing.InOutQuad(t - .5f, 1, 0, .5f));
                    }
            });

            SetSuitMeshes(suitsNew);
            suitsPrev = suitsNew;
        }
    }

    private void SetSuitMeshes(int[] suits)
    {
        for (var i = 0; i < suits.Length; i++)
        {
            SuitObjects[i].transform.localPosition = new Vector3(SuitX(i), -0.01f, 0);
            SuitObjects[i].transform.localRotation = Quaternion.identity;
            SuitObjects[i].sharedMesh = SuitMeshes[4 * _jumps[Array.IndexOf(_suitsGoal, suits[i])] + suits[i]];
            SuitRenderers[i].material = _maskMaterials.DiffuseTint;
            SuitRenderers[i].material.color = new Color32(0x92, 0xB7, 0xFF, 0xFF);
        }
        for (var i = 0; i < SuitSeparators.Length; i++)
        {
            SuitSeparators[i].material = _maskMaterials.DiffuseTint;
            SuitSeparators[i].material.color = new Color32(0x92, 0xB7, 0xFF, 0xFF);
        }
    }

    private static float SuitX(int i)
    {
        return -0.114f + .076f * i;
    }

    private IEnumerator AnimateWordsAndSolve(Func<bool> stop)
    {
        WordResultText.GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;
        for (var i = 0; i < 3; i++)
            WordTexts[i].GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;

        while (!stop())
        {
            if (_stage == Stage.Solved)
            {
                WordResultText.text = _word;

                for (var i = 0; i < 3; i++)
                    WordTexts[i].gameObject.SetActive(false);

                yield return Animation(2.6f, t =>
                {
                    WordResultText.transform.localPosition = Vector3.Lerp(new Vector3(0, -.02f, -.03f), new Vector3(0, 0, 0), Easing.InOutQuad(t, 0, 1, 1));
                    WordResultText.transform.localScale = Vector3.Lerp(new Vector3(.015f, .015f, .015f), new Vector3(.025f, .025f, .025f), Easing.InOutQuad(t, 0, 1, 1));
                    WordResultText.color = Color.Lerp(new Color32(0xE1, 0xE1, 0xE1, 0xFF), new Color32(0x0D, 0xE1, 0x0F, 0xFF), Easing.InOutQuad(t, 0, 1, 1));
                });
                yield break;
            }
            else
            {
                _wordHighlight = (int) ((Time.time % 1.8f) / 1.8f * 3);
                for (var i = 0; i < 3; i++)
                {
                    WordTexts[i].gameObject.SetActive(true);
                    WordTexts[i].color = i == _wordHighlight ? Color.white : (Color) new Color32(0x3D, 0x69, 0xC7, 0xFF);
                }
                WordResultText.text = _word.Substring(0, _wordProgress) + "_";

                if (_wordSection == 0)
                {
                    WordTexts[0].text = "A–I";
                    WordTexts[1].text = "J–R";
                    WordTexts[2].text = "S–Z";
                }
                else if (_wordSection <= 3)
                {
                    for (var triplet = 0; triplet < 3; triplet++)
                        WordTexts[triplet].text = _wordSection == 3 && triplet == 2 ? "YZ" : Enumerable.Range(0, 3).Select(ltr => (char) ('A' + (_wordSection - 1) * 9 + 3 * triplet + ltr)).Join("");
                }
                else
                {
                    for (var ltr = 0; ltr < 3; ltr++)
                        WordTexts[ltr].text = _wordSection == 12 && ltr == 2 ? "" : ((char) ('A' + ((_wordSection - 4) * 3 + ltr))).ToString();
                }

                yield return null;
            }
        }

        for (var i = 0; i < 3; i++)
            WordTexts[i].gameObject.SetActive(false);
    }

    private IEnumerator AnimateReset(Func<bool> stop)
    {
        ResetText.GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;
        ResetText.gameObject.SetActive(true);
        while (!stop())
            yield return null;
        ResetText.gameObject.SetActive(false);
    }

    private GameObject MakeGameObject(string name, Transform parent, float scale, Vector3? position = null, Quaternion? rotation = null)
    {
        return MakeGameObject(name, parent, position, rotation, scale: new Vector3(scale, scale, scale));
    }
    private GameObject MakeGameObject(string name, Transform parent, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
    {
        var obj = new GameObject(name);
        obj.transform.parent = parent;
        obj.transform.localPosition = position ?? new Vector3(0, 0, 0);
        obj.transform.localRotation = rotation ?? Quaternion.identity;
        obj.transform.localScale = scale ?? new Vector3(1, 1, 1);
        return obj;
    }

    private static readonly string[] _svgColors = { "#5065DE", "#37923F", "#3F9DA5", "#C33E3E", "#B439AC", "#B8BA3C" };
    private void GeneratePuzzle()
    {
        var puzzle = BlueButtonPuzzle.GeneratePuzzle(Rnd.Range(0, int.MaxValue));
        _polyominoSequence = puzzle.PolyominoSequence;
        _polyominoSequenceColors = puzzle.PolyominoSequenceColors;
        _colorStageColors = puzzle.ColorStageColors;
        _equationOffsets = puzzle.EquationOffsets;
        _equations = _equationOffsets.Select(offset => GenerateEquation(offset)).ToArray();
        _suitsGoal = puzzle.Suits;
        _jumps = puzzle.Jumps;
        _word = puzzle.Word;

        _suitsCurrent = Enumerable.Range(0, 4).ToArray().Shuffle();

        var polyominoSeqSvg = new StringBuilder();
        var polyominoSolutionSvg = new StringBuilder();
        var x = 0d;
        var maxHeight = 0;
        for (var i = 0; i < _polyominoSequence.Length; i++)
        {
            var place = _polyominoSequence[i].Place;
            var height = _polyominoSequence[i].Polyomino.Cells.Max(c => c.Y) + 1;
            maxHeight = Math.Max(maxHeight, height);
            foreach (var block in _polyominoSequence[i].Polyomino.Cells)
            {
                polyominoSeqSvg.Append(string.Format("<rect fill='{0}' x='{1}' y='{2}' width='1' height='1' />", _svgColors[_polyominoSequenceColors[i]], x + block.X, block.Y - height * .5));
                polyominoSolutionSvg.Append(string.Format("<rect fill='{0}' x='{1}' y='{2}' width='1' height='1' />", _svgColors[_polyominoSequenceColors[i]], place.AddXWrap(block.X).X, place.AddYWrap(block.Y).Y));
            }
            x += _polyominoSequence[i].Polyomino.Cells.Max(c => c.X) + 1.5;
        }

        Debug.LogFormat(@"[The Blue Button #{0}]=svg[Stage 1 polyominoes:]<svg xmlns='http://www.w3.org/2000/svg' viewBox='-.1 {1} {2} {3}' stroke='black' stroke-width='.06'>{4}</svg>",
            _moduleId, -maxHeight * .5 - .1, x - .5 + .2, maxHeight + .2, polyominoSeqSvg);

        var segs = new List<Seg>();
        for (var cellIx = 0; cellIx < BlueButtonPuzzle.GridWidth * BlueButtonPuzzle.GridHeight; cellIx++)
        {
            var cell = new Coord(BlueButtonPuzzle.GridWidth, BlueButtonPuzzle.GridHeight, cellIx);
            if (puzzle.PolyominoGrid[cellIx] != puzzle.PolyominoGrid[cell.AddXWrap(1).Index])
            {
                segs.Add(new Seg { d1 = 2, d2 = 0, c = new List<int> { (cell.X + 1) | (cell.Y << 3), (cell.X + 1) | ((cell.Y + 1) << 3) } });
                if (cell.X == BlueButtonPuzzle.GridWidth - 1)
                    segs.Add(new Seg { d1 = 2, d2 = 0, c = new List<int> { cell.Y << 3, (cell.Y + 1) << 3 } });
            }
            if (puzzle.PolyominoGrid[cellIx] != puzzle.PolyominoGrid[cell.AddYWrap(1).Index])
            {
                segs.Add(new Seg { d1 = 1, d2 = 3, c = new List<int> { cell.X | ((cell.Y + 1) << 3), (cell.X + 1) | ((cell.Y + 1) << 3) } });
                if (cell.Y == BlueButtonPuzzle.GridHeight - 1)
                    segs.Add(new Seg { d1 = 1, d2 = 3, c = new List<int> { cell.X, cell.X + 1 } });
            }
        }

        for (var i = 0; i < 5; i++)
            polyominoSolutionSvg.AppendFormat("<circle cx='{0}' cy='{1}' r='.4' stroke='white' stroke-width='.1' fill='none' />", i + .5, i == 4 ? .5 : _jumps[i] + .5);

        Debug.LogFormat(@"[The Blue Button #{0}]=svg[Stage 1 solution:]<svg xmlns='http://www.w3.org/2000/svg' viewBox='-.1 -.1 {2} 4.2'>{3}<path stroke='black' stroke-width='.06' fill='none' d='{1}' /></svg>",
            _moduleId, GeneratePolyominoSolutionSvgPath(segs), x - .5 + .2, polyominoSolutionSvg);

        Debug.LogFormat(@"[The Blue Button #{0}] Stage 2: Color sequence is: {1}", _moduleId, _colorStageColors.Select(c => _colorNames[c]).Join(", "));
        Debug.LogFormat(@"[The Blue Button #{0}] Stage 2: Submit on {1} after the {2}.", _moduleId, _colorNames[_colorStageColors[3]], _colorStageColors.Take(3).Select(c => _colorNames[c]).Join(", "));

        var explanations = "number of colors;position of diamonds;position within key polyomino;number of taps to submit".Split(';');
        for (var i = 0; i < _equations.Length; i++)
            Debug.LogFormat(@"[The Blue Button #{0}] Stage 3: Equation #{1}: {2} (offset {3} = {4}).", _moduleId, i + 1, _equations[i], _equationOffsets[i], explanations[i]);

        Debug.LogFormat(@"[The Blue Button #{0}] Stage 4: Suits are shown as {1}. Desired order is {2}.", _moduleId, _suitsCurrent.Select(s => "♠♥♣♦"[s]).Join(""), _suitsGoal.Select(s => "♠♥♣♦"[s]).Join(""));
        Debug.LogFormat(@"[The Blue Button #{0}] Stage 5: The solution word is {1}.", _moduleId, _word);
    }

    struct Seg
    {
        public int d1, d2;
        public List<int> c;
    }

    private string GeneratePolyominoSolutionSvgPath(List<Seg> segs)
    {
        var svg = new StringBuilder();
        while (segs.Count > 0)
        {
            var seg = segs[segs.Count - 1];
            segs.RemoveAt(segs.Count - 1);

            while (true)
            {
                var extIx = segs.IndexOf(sg =>
                    ((sg.d1 ^ 2) == seg.d1 && sg.c[0] == seg.c[0]) ||
                    ((sg.d1 ^ 2) == seg.d2 && sg.c[0] == seg.c[seg.c.Count - 1]) ||
                    ((sg.d2 ^ 2) == seg.d1 && sg.c[sg.c.Count - 1] == seg.c[0]) ||
                    ((sg.d2 ^ 2) == seg.d2 && sg.c[sg.c.Count - 1] == seg.c[seg.c.Count - 1]));
                if (extIx == -1)
                {
                    extIx = segs.IndexOf(sg =>
                        ((sg.c[0] == seg.c[0] || sg.c[0] == seg.c[seg.c.Count - 1]) &&
                            !segs.Any(s => (s.c[0] == sg.c[0] && (s.d1 ^ 2) == sg.d1) || (s.c[s.c.Count - 1] == sg.c[0] && (s.d2 ^ 2) == sg.d1))) ||
                        ((sg.c[sg.c.Count - 1] == seg.c[0] || sg.c[sg.c.Count - 1] == seg.c[seg.c.Count - 1]) &&
                            !segs.Any(s => (s.c[0] == sg.c[sg.c.Count - 1] && (s.d1 ^ 2) == sg.d2) || (s.c[s.c.Count - 1] == sg.c[sg.c.Count - 1] && (s.d2 ^ 2) == sg.d2))));
                }
                if (extIx == -1)
                    break;
                var ext = segs[extIx];
                segs.RemoveAt(extIx);
                if (seg.c[0] == ext.c[0])
                {
                    seg.c.Reverse();
                    seg.c.RemoveAt(seg.c.Count - 1);
                    if (seg.d1 == (ext.d1 ^ 2))
                        ext.c.RemoveAt(0);
                    seg.c.AddRange(ext.c);
                    seg.d1 = seg.d2;
                    seg.d2 = ext.d2;
                }
                else if (seg.c[0] == ext.c[ext.c.Count - 1])
                {
                    ext.c.RemoveAt(ext.c.Count - 1);
                    if (seg.d1 == (ext.d2 ^ 2))
                        seg.c.RemoveAt(0);
                    ext.c.AddRange(seg.c);
                    ext.d2 = seg.d2;
                    seg = ext;
                }
                else if (seg.c[seg.c.Count - 1] == ext.c[0])
                {
                    seg.c.RemoveAt(seg.c.Count - 1);
                    if (ext.d1 == (seg.d2 ^ 2))
                        ext.c.RemoveAt(0);
                    seg.c.AddRange(ext.c);
                    seg.d2 = ext.d2;
                }
                else if (seg.c[seg.c.Count - 1] == ext.c[ext.c.Count - 1])
                {
                    ext.c.Reverse();
                    seg.c.RemoveAt(seg.c.Count - 1);
                    if (seg.d2 == (ext.d2 ^ 2))
                        ext.c.RemoveAt(0);
                    seg.c.AddRange(ext.c);
                    seg.d2 = ext.d1;
                }
            }
            svg.Append("M");
            for (var pIx = 0; pIx < seg.c.Count; pIx++)
            {
                var p = seg.c[pIx];
                if (pIx == seg.c.Count - 1 && p == seg.c[0])
                    svg.Append("z");
                else
                {
                    svg.Append(" ");
                    svg.Append(p & 7);
                    svg.Append(" ");
                    svg.Append(p >> 3);
                }
            }
        }
        return svg.ToString();
    }

    private string GenerateEquation(int offset)
    {
        int op1, op2;
        switch (Rnd.Range(0, 3))
        {
            case 0:
                op1 = Rnd.Range(11, 90);
                op2 = Rnd.Range(11, 90);
                return string.Format("{0} + {1} = {2}", op1, op2, op1 + op2 <= offset || Rnd.Range(0, 2) != 0 ? op1 + op2 + offset : op1 + op2 - offset);

            case 1:
                op1 = Rnd.Range(11, 90);
                op2 = Rnd.Range(11, 90);
                var sm = Math.Min(op1, op2);
                var la = Math.Max(op1, op2);
                return string.Format("{0} − {1} = {2}", la, sm, la - sm <= offset || Rnd.Range(0, 2) != 0 ? la - sm + offset : la - sm - offset);

            default:
                op1 = Rnd.Range(3, 10);
                op2 = Rnd.Range(3, 10);
                return string.Format("{0} × {1} = {2}", op1, op2, op1 * op2 <= offset || Rnd.Range(0, 2) != 0 ? op1 * op2 + offset : op1 * op2 - offset);
        }
    }

    private IEnumerator EquationTimeout()
    {
        yield return new WaitForSeconds(1.5f);
        if (_eqTaps != _equationOffsets[3])
        {
            Debug.LogFormat(@"[The Blue Button #{0}] Stage 3: You tapped the button {1} times instead of {2}. Strike!", _moduleId, _eqTaps, _equationOffsets[3]);
            Module.HandleStrike();
        }
        else
        {
            _stage = Stage.Suits;
            _suitTapIx = 0;
        }
        _eqTaps = 0;
    }

    private IEnumerator AnimateButton(float a, float b)
    {
        var duration = 0.1f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            BlueButtonCap.transform.localPosition = new Vector3(0f, Easing.InOutQuad(elapsed, a, b, duration), 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        BlueButtonCap.transform.localPosition = new Vector3(0f, b, 0f);
    }

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} tap [stage 1] | !{0} tap RGBY [stage 2: wait for RGBY and tap on Y] | !{0} tap 5 [stage 3: tap 5 times] | !{0} tap 1 2 3 [stage 4: tap with relative time intervals] | !{0} tap 1 3 2 3 1 [stage 5: tap when the highlight is in these positions] | !{0} reset";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        if (Regex.IsMatch(command, @"^\s*reset\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.75f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            yield break;
        }

        if (_stage == Stage.Polyominoes && Regex.IsMatch(command, @"^\s*tap\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            yield break;
        }

        Match m;
        if (_stage == Stage.Colors && (m = Regex.Match(command, @"^\s*tap\s+([ BGCRMY]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            var colStr = "BGCRMY";
            var cmd = m.Groups[1].Value.Replace(" ", "").ToUpperInvariant();
            var ix = Enumerable.Range(0, _colorStageColors.Length).IndexOf(colIx => Enumerable.Range(0, cmd.Length).All(cmdIx => _colorStageColors[(colIx + cmdIx) % _colorStageColors.Length] == colStr.IndexOf(cmd[cmdIx])));
            if (ix == -1)
            {
                yield return "sendtochaterror That sequence of colors is not there.";
                yield break;
            }
            yield return null;
            while (_colorHighlight != (ix + cmd.Length - 1) % _colorStageColors.Length)
                yield return null;
            BlueButtonSelectable.OnInteract();
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            yield break;
        }

        if (_stage == Stage.Equations && (m = Regex.Match(command, @"^\s*tap\s+([1-9])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            int val;
            if (!int.TryParse(m.Groups[1].Value, out val))
            {
                yield return "sendtochaterror How many times should I tap it?";
                yield break;
            }
            yield return null;
            for (; val > 0; val--)
            {
                BlueButtonSelectable.OnInteract();
                yield return new WaitForSeconds(.1f);
                BlueButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(.1f);
            }
            yield break;
        }

        if (_stage == Stage.Suits && (m = Regex.Match(command, @"^\s*tap\s+([0-9])\s*([0-9])\s*([0-9])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            int len1, len2, len3;
            if (!int.TryParse(m.Groups[1].Value, out len1) ||
                !int.TryParse(m.Groups[2].Value, out len2) ||
                !int.TryParse(m.Groups[3].Value, out len3))
            {
                yield return "sendtochaterror Be more specific on the time intervals?";
                yield break;
            }
            yield return null;

            _tpOverrideRAB = new[] { len1, len2, len3 };
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f + .2f * len1);
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f + .2f * len2);
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f + .2f * len3);
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            if (_tpOverriddenRAB)
                Debug.LogFormat("<The Blue Button #{0}> TP lag override applied.", _moduleId);
            _tpOverriddenRAB = false;
            _tpOverrideRAB = null;
            yield break;
        }

        if (_stage == Stage.Word && (m = Regex.Match(command, @"^\s*tap\s+([,; 123]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            yield return null;
            foreach (var ch in m.Groups[1].Value)
            {
                if (ch < '1' || ch > '3')
                    continue;
                while (_wordHighlight != ch - '1')
                    yield return null;
                BlueButtonSelectable.OnInteract();
                BlueButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(.2f);
            }
            yield break;
        }
    }

    public IEnumerator TwitchHandleForcedSolve()
    {
        if (_stage == Stage.Polyominoes)
        {
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(1.5f);
        }

        if (_stage == Stage.Colors)
        {
            while (_colorHighlight != 3)
                yield return true;
            BlueButtonSelectable.OnInteract();
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(1.5f);
        }

        if (_stage == Stage.Equations)
        {
            for (var val = _equationOffsets[3] - _eqTaps; val > 0; val--)
            {
                BlueButtonSelectable.OnInteract();
                yield return new WaitForSeconds(.1f);
                BlueButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(.1f);
            }
            yield return new WaitForSeconds(1.4f);
        }

        while (_stage == Stage.Suits)
        {
            // Find the first slot that currently has a wrong suit in it
            var firstWrongSlot = Enumerable.Range(0, 4).IndexOf(i => _suitsGoal[i] != _suitsCurrent[i]);

            // Assume we want to submit
            var len1 = 2;
            var len2 = 3;
            var len3 = 3;
            var submitting = true;

            if (firstWrongSlot != -1)
            {
                // Find where the desired suit is at the moment.
                // We’re swapping that with the slot LEFT of it
                var swapSlot = Array.IndexOf(_suitsCurrent, _suitsGoal[firstWrongSlot]) - 1;

                submitting = false;
                len2 = swapSlot == 0 ? 1 : swapSlot == 1 ? 3 : swapSlot == 2 ? 1 : 3;
                len3 = swapSlot == 0 ? 1 : swapSlot == 1 ? 1 : swapSlot == 2 ? 3 : 3;
            }

            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f + .2f * len1);
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f + .2f * len2);
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f + .2f * len3);
            BlueButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(submitting ? 1.4f : .1f);
        }

        while (_stage == Stage.Word)
        {
            var ltr = _word[_wordProgress] - 'A';
            var requiredHighlight =
                _wordSection == 0 ? ltr / 9 :
                _wordSection <= 3 ? (ltr / 3) % 3 : ltr % 3;

            while (_wordHighlight != requiredHighlight)
                yield return true;
            BlueButtonSelectable.OnInteract();
            BlueButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.2f);
        }
    }
}
