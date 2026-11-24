// The handlers of the event subscriptions NoteBeforeWrite and NoteFormGet. The module runs on the server, and an
// external connection lacks it: it has no External connection flag, so that the check of the console names
// NoteBeforeWrite, and a Write that wrote a note would fail. Nothing writes a note.

Procedure NoteBeforeWrite(Source, Cancel) Export

EndProcedure

Procedure NoteFormGet(Source, FormType, Parameters, SelectedForm, AdditionalInformation, StandardProcessing) Export

EndProcedure
