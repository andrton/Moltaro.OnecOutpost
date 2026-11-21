// The executor of Moltaro.OnecOutpost. The COM worker opens a fresh object of this external data
// processor for each command, in safe mode or without it, calls Run once and releases the object
// (concept section 3). The module uses only what the compatibility mode 8.2.13 allows.
//
// Run(Mode, Script, ParametersJson, CallerKey, OutcomeLimit) runs the script and returns its
// outcome:
// - Mode is "read" or "write". ParametersJson is the text of a JSON object. CallerKey is a string
//   or Undefined. OutcomeLimit is a number of bytes.
// - The script runs with Execute and sees Parameters, read from ParametersJson into a Map,
//   CallerKey and Result (concept section 3). A Write runs in one transaction, with the checks of
//   rule 5 before its commit.
// - It returns one line of text without a line break:
//   {"size":<number>,"outcome":<the outcome object of docs/protocol.md section 8>}. size is the
//   length in bytes of the UTF-8 text of the outcome object alone, as the executor measured it
//   before the commit of a Write. The object is written with the line breaks of the JSON writer
//   turned off.
// - Before it writes and measures the object, it replaces each U+0000 and each lone surrogate in
//   its messages and its error text with U+FFFD, then trims them to fit the limit, never inside a
//   surrogate pair. It replaces Null with Undefined before it writes a result, since WriteJSON
//   refuses Null.
// - Safe mode is not its parameter: the worker opens the executor in safe mode or without it.
// - It never raises: every error of the script is an outcome.
//
// Not implemented yet: Run raises an error that says so.

Function Run(Mode, Script, ParametersJson, CallerKey, OutcomeLimit) Export

	Raise "OutpostExecutor.Run is not implemented yet.";

EndFunction
