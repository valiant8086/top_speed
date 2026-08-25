using System;
using System.Runtime.InteropServices;
using TopSpeed.Runtime;
using TopSpeed.Windowing.Sdl.Cocoa;
using SdlWindow = TS.Sdl.Video.Window;

namespace TopSpeed.Windowing.Sdl
{
    /// <summary>
    /// Puts a real Cocoa text field inside the game window while the player is being asked for
    /// something, the same way the Windows build puts a WinForms text box inside its form.
    ///
    /// The SDL window is a single blank surface with no controls in it, so VoiceOver finds nothing
    /// to read while the game collects keys itself. An NSTextField added to the window's content
    /// view is an ordinary control in an ordinary window, so VoiceOver reads it, echoes typing and
    /// gives review by character and word, cursor movement and correction for free.
    ///
    /// Every method here has to run on the thread that owns the window; the window host marshals.
    /// </summary>
    internal sealed class MacTextPrompt
    {
        // NSViewWidthSizable | NSViewMinYMargin | NSViewMaxYMargin: the field follows the window's
        // width and stays centred vertically when the player resizes the window.
        private const ulong AutoresizeWidthAndCentre = 2 | 8 | 32;
        private const double FieldHeight = 24;
        private const double SideMargin = 20;

        private static readonly object ClassLock = new object();
        private static IntPtr _handlerClass;

        // The runtime only holds the function pointers, so the delegates have to be kept alive here
        // or they are collected while Cocoa still has their addresses.
        private static ActionHandler? _actionHandler;
        private static CommandHandler? _commandHandler;

        // Only one prompt is ever up at a time, and the callbacks arrive with no state of their own.
        private static MacTextPrompt? _active;

        private readonly Action<TextInputResult> _onCompleted;
        private IntPtr _field;
        private IntPtr _handler;
        private IntPtr _contentView;
        private IntPtr _cocoaWindow;
        private IntPtr _previousResponder;

        public MacTextPrompt(Action<TextInputResult> onCompleted)
        {
            _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
        }

        public static bool IsSupported => OperatingSystem.IsMacOS();

        public bool IsShowing => _field != IntPtr.Zero;

        /// <summary>
        /// Adds the field and gives it the keyboard. False if the window turns out not to have a
        /// Cocoa window behind it, which leaves the caller to collect keys itself.
        /// </summary>
        public bool Show(IntPtr sdlWindow, string prompt, string? initialText)
        {
            if (!IsSupported || sdlWindow == IntPtr.Zero || IsShowing)
                return false;

            var cocoaWindow = SdlWindow.GetCocoaWindow(sdlWindow);
            if (cocoaWindow == IntPtr.Zero)
                return false;

            var contentView = ObjC.Send(cocoaWindow, ObjC.Selector("contentView"));
            if (contentView == IntPtr.Zero)
                return false;

            var handlerClass = EnsureHandlerClass();
            if (handlerClass == IntPtr.Zero)
                return false;

            SdlWindow.GetSize(sdlWindow, out var width, out var height);
            if (width <= 0 || height <= 0)
                return false;

            var frame = new CGRect(
                SideMargin,
                Math.Max(0, (height - FieldHeight) / 2),
                Math.Max(FieldHeight, width - (SideMargin * 2)),
                FieldHeight);

            var field = ObjC.SendRect(
                ObjC.Send(ObjC.GetClass("NSTextField"), ObjC.Selector("alloc")),
                ObjC.Selector("initWithFrame:"),
                frame);
            if (field == IntPtr.Zero)
                return false;

            _cocoaWindow = cocoaWindow;
            _contentView = contentView;
            _field = field;
            _handler = ObjC.Send(ObjC.Send(handlerClass, ObjC.Selector("alloc")), ObjC.Selector("init"));

            ConfigureField(prompt, initialText);

            // Remembered so the game gets the keyboard back exactly as it had it.
            _previousResponder = ObjC.Send(cocoaWindow, ObjC.Selector("firstResponder"));

            ObjC.SendVoid(contentView, ObjC.Selector("addSubview:"), field);
            ObjC.SendBool(cocoaWindow, ObjC.Selector("makeFirstResponder:"), field);

            // A prompt the player cannot see or type into is worse than none, so make sure the
            // window is the one taking keys even if the game was in the background.
            var application = ObjC.Send(ObjC.GetClass("NSApplication"), ObjC.Selector("sharedApplication"));
            if (application != IntPtr.Zero)
                ObjC.SendVoidBool(application, ObjC.Selector("activateIgnoringOtherApps:"), true);
            ObjC.SendVoid(cocoaWindow, ObjC.Selector("makeKeyAndOrderFront:"), IntPtr.Zero);

            _active = this;
            return true;
        }

        /// <summary>Takes the field away and hands the keyboard back to the game.</summary>
        public void Hide()
        {
            if (!IsShowing)
                return;

            var field = _field;
            var handler = _handler;
            _field = IntPtr.Zero;
            _handler = IntPtr.Zero;
            if (ReferenceEquals(_active, this))
                _active = null;

            // Dropped before the field goes away so Cocoa cannot call back into a released handler.
            ObjC.SendVoid(field, ObjC.Selector("setDelegate:"), IntPtr.Zero);
            ObjC.SendVoid(field, ObjC.Selector("setTarget:"), IntPtr.Zero);
            ObjC.SendVoid(field, ObjC.Selector("removeFromSuperview"));

            if (_cocoaWindow != IntPtr.Zero)
                ObjC.SendBool(_cocoaWindow, ObjC.Selector("makeFirstResponder:"), _previousResponder);

            ObjC.Release(field);
            ObjC.Release(handler);
            _contentView = IntPtr.Zero;
            _cocoaWindow = IntPtr.Zero;
            _previousResponder = IntPtr.Zero;
        }

        private void ConfigureField(string prompt, string? initialText)
        {
            var text = ObjC.NewString(initialText ?? string.Empty);
            ObjC.SendVoid(_field, ObjC.Selector("setStringValue:"), text);
            ObjC.Release(text);

            // What the game asked for is the field's name as far as VoiceOver is concerned; the
            // game has already spoken it, and this is what gets read again on review.
            var label = ObjC.NewString(prompt ?? string.Empty);
            ObjC.SendVoid(_field, ObjC.Selector("setAccessibilityLabel:"), label);
            ObjC.SendVoid(_field, ObjC.Selector("setPlaceholderString:"), label);
            ObjC.Release(label);

            ObjC.SendVoidBool(_field, ObjC.Selector("setEditable:"), true);
            ObjC.SendVoidBool(_field, ObjC.Selector("setSelectable:"), true);
            ObjC.SendVoidBool(_field, ObjC.Selector("setBezeled:"), true);
            ObjC.SendVoidUInt(_field, ObjC.Selector("setAutoresizingMask:"), AutoresizeWidthAndCentre);

            ObjC.SendVoid(_field, ObjC.Selector("setTarget:"), _handler);
            ObjC.SendVoid(_field, ObjC.Selector("setAction:"), ObjC.Selector("textFieldAction:"));
            ObjC.SendVoid(_field, ObjC.Selector("setDelegate:"), _handler);
        }

        private string ReadText()
        {
            return _field == IntPtr.Zero
                ? string.Empty
                : ObjC.ReadString(ObjC.Send(_field, ObjC.Selector("stringValue")));
        }

        private void Complete(TextInputResult result)
        {
            Hide();
            _onCompleted(result);
        }

        // Cocoa sends the field's action when the player presses Return.
        private static void OnAction(IntPtr self, IntPtr command, IntPtr sender)
        {
            var prompt = _active;
            if (prompt == null)
                return;

            prompt.Complete(TextInputResult.Submitted(prompt.ReadText()));
        }

        // Escape reaches the field editor as cancelOperation:, and never as a key the field would
        // otherwise report. Return is handled by the action above, but it arrives here too on a
        // field that is not sending its action, so both are answered.
        private static byte OnDoCommand(IntPtr self, IntPtr command, IntPtr control, IntPtr textView, IntPtr selector)
        {
            var prompt = _active;
            if (prompt == null)
                return 0;

            if (selector == ObjC.Selector("cancelOperation:"))
            {
                prompt.Complete(TextInputResult.CreateCancelled());
                return 1;
            }

            if (selector == ObjC.Selector("insertNewline:"))
            {
                prompt.Complete(TextInputResult.Submitted(prompt.ReadText()));
                return 1;
            }

            return 0;
        }

        // A tiny Objective-C class that exists only to be the field's target and delegate. There is
        // no way to hand Cocoa a bare function, so one is built at runtime the first time it is
        // needed and kept for the life of the process.
        private static IntPtr EnsureHandlerClass()
        {
            lock (ClassLock)
            {
                if (_handlerClass != IntPtr.Zero)
                    return _handlerClass;

                var root = ObjC.GetClass("NSObject");
                if (root == IntPtr.Zero)
                    return IntPtr.Zero;

                var created = ObjC.AllocateClassPair(root, "TopSpeedTextPromptHandler", IntPtr.Zero);
                if (created == IntPtr.Zero)
                {
                    // Already registered, which happens if this ran once before in this process.
                    _handlerClass = ObjC.GetClass("TopSpeedTextPromptHandler");
                    return _handlerClass;
                }

                _actionHandler = OnAction;
                _commandHandler = OnDoCommand;

                ObjC.AddMethod(
                    created,
                    ObjC.Selector("textFieldAction:"),
                    Marshal.GetFunctionPointerForDelegate(_actionHandler),
                    "v@:@");
                ObjC.AddMethod(
                    created,
                    ObjC.Selector("control:textView:doCommandBySelector:"),
                    Marshal.GetFunctionPointerForDelegate(_commandHandler),
                    "c@:@@:");

                ObjC.RegisterClassPair(created);
                _handlerClass = created;
                return _handlerClass;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ActionHandler(IntPtr self, IntPtr command, IntPtr sender);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte CommandHandler(IntPtr self, IntPtr command, IntPtr control, IntPtr textView, IntPtr selector);
    }
}
