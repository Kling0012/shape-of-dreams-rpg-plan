using UnityEngine;

namespace SodRpg.Mod
{
    internal sealed class ImeInput
    {
        internal static bool Editing { get; private set; }

        private string _controlName;
        private int _keyboardControl;
        private IMECompositionMode _previousMode;
        private bool _drawn, _mouseDown, _fieldMouseDown, _clickedField;
        private int _compositionFrame = -10;

        public bool Focused => _controlName != null;

        // IME can clear compositionString in the frame that delivers its confirm/cancel key.
        public bool Composing
        {
            get
            {
                if (Input.compositionString.Length > 0) _compositionFrame = Time.frameCount;
                return Time.frameCount - _compositionFrame <= 1;
            }
        }

        public void BeginDraw()
        {
            _drawn = false;
            _mouseDown = Event.current.type == EventType.MouseDown;
            _clickedField = false;
        }

        public void BeginField(string controlName)
        {
            _fieldMouseDown = Event.current.type == EventType.MouseDown;
            GUI.SetNextControlName(controlName);
        }

        public void Track(string controlName)
        {
            string focused = GUI.GetNameOfFocusedControl();
            if (_fieldMouseDown && Event.current.type == EventType.Used && focused == controlName) _clickedField = true;
            if (Focused && (focused != _controlName || !GUI.enabled)) Release(false);
            if (!GUI.enabled || focused != controlName) return;
            if (!Focused)
            {
                _previousMode = Input.imeCompositionMode;
                _controlName = controlName;
                _keyboardControl = GUIUtility.keyboardControl;
                Editing = true;
            }
            _drawn = true;
            Input.imeCompositionMode = IMECompositionMode.On;
            if (Input.compositionString.Length > 0) _compositionFrame = Time.frameCount;
        }

        public void EndDraw()
        {
            // The codex deliberately skips its fixed-position controls during Layout.
            if (Event.current.type != EventType.Layout
                && (!_drawn || (_mouseDown && !_clickedField) || GUI.GetNameOfFocusedControl() != _controlName)) Release();
        }

        public void Release(bool clearFocus = true)
        {
            if (!Focused) return;
            Input.imeCompositionMode = _previousMode;
            _controlName = null;
            if (clearFocus && GUIUtility.keyboardControl == _keyboardControl) GUIUtility.keyboardControl = 0;
            _keyboardControl = 0;
            Editing = false;
            _compositionFrame = -10;
        }
    }
}
