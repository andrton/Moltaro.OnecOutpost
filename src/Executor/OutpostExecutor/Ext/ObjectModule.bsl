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
//   rule 5 before its commit. A Write whose script ended the transaction itself, or whose commit
//   raised, is Unknown transaction-ended; one whose script left a transaction of its own open is
//   rolled back and fails with script-error and an error text of the executor.
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
// How the size is measured. The module composes the outcome object itself, so that its size is
// the sum of the sizes of its parts. The JSON writer of 1C writes the result, and the text it wrote
// is measured. The module writes the error text and the messages as section 2 of
// docs/protocol.md writes a string, so it knows the size of every character before it writes it,
// and trims a text by that size. The compatibility mode 8.2.13 has no function that encodes a text
// as UTF-8, so the bytes are counted from the codes of the characters: a 1C string is a sequence
// of UTF-16 code units, and a surrogate pair is one character of four bytes.

Function Run(Mode, Script, ParametersJson, CallerKey, OutcomeLimit) Export

	IsWrite = (Mode = "write");

	// The messages of an earlier command of the session are not this command's (concept section
	// 12, O7).
	GetUserMessages(True);

	If IsWrite Then
		BeginTransaction();
	EndIf;

	ScriptFailed = False;
	ErrorText = Undefined;
	Result = Undefined;

	Try
		Result = ScriptResult(Script, ParametersOf(ParametersJson), CallerKey);
	Except
		ScriptFailed = True;
		ErrorText = DetailErrorDescription(ErrorInfo());
	EndTry;

	// Collected before the checks, since they count in the size of the outcome (concept rule 5).
	Messages = UserMessages();

	// A script that ended the transaction of its Write itself, with CommitTransaction or
	// RollbackTransaction, voids rule 5 for it: whether it wrote anything cannot be known.
	If IsWrite And Not TransactionActive() Then
		Return UnknownLine(OutcomeLimit);
	EndIf;

	If ScriptFailed Then
		Return FailedLine("script-error", ErrorText, Messages, IsWrite, OutcomeLimit);
	EndIf;

	ResultJson = ResultJsonOf(Result);

	If ResultJson = Undefined Then
		Return FailedLine("result-type", Undefined, Messages, IsWrite, OutcomeLimit);
	EndIf;

	Outcome = OutcomeOf("succeeded", Undefined, ResultJson, Utf8Size(ResultJson), Undefined, Messages, OutcomeLimit);

	If Outcome.Size > OutcomeLimit Then
		Return FailedLine("result-too-large", Undefined, Messages, IsWrite, OutcomeLimit);
	EndIf;

	If IsWrite Then
		Return CommittedLine(Outcome, Messages, OutcomeLimit);
	EndIf;

	Return LineOf(Outcome);

EndFunction

// Runs the script with Execute in a context of its own, so that the script sees Parameters,
// CallerKey and Result and none of the variables of Run. A script that sets no result leaves it
// Undefined.
Function ScriptResult(Script, Parameters, CallerKey)

	Result = Undefined;
	Execute(Script);

	Return Result;

EndFunction

// Reads the parameters into a Map with string keys: an object becomes a Map, an array an Array,
// null Undefined, and a string stays a string, one that looks like a date too, since no member is
// named as a date (concept section 3).
Function ParametersOf(ParametersJson)

	Reader = New JSONReader;
	Reader.SetString(ParametersJson);
	Parameters = ReadJSON(Reader, True);
	Reader.Close();

	Return Parameters;

EndFunction

// The texts of the user messages of this command, in their order, cleaned; reading them deletes
// them from the session.
Function UserMessages()

	Messages = New Array;

	For Each UserMessage In GetUserMessages(True) Do
		Messages.Add(CleanText(UserMessage.Text));
	EndDo;

	Return Messages;

EndFunction

// Commits the transaction of the Write and returns the line of its outcome, which the checks of
// rule 5 have passed.
Function CommittedLine(Outcome, Messages, OutcomeLimit)

	Try
		CommitTransaction();
	Except
		// A commit that raised gives Unknown, whether 1C still holds the transaction afterwards or
		// has ended it: the executor cannot tell whether the Write was written. A transaction that
		// 1C still holds is rolled back, so that the next command of the session does not run in it.
		RollBackEveryTransaction();
		Return UnknownLine(OutcomeLimit);
	EndTry;

	// The script began a transaction of its own inside the Write and did not end it, so the commit
	// above committed nothing: 1C commits when every transaction begun has ended. Rolled back,
	// nothing is written, and the Write fails. 1C raised nothing, so the text is the executor's.
	If TransactionActive() Then
		Return FailedLine("script-error", "The script left a transaction open.", Messages, True, OutcomeLimit);
	EndIf;

	Return LineOf(Outcome);

EndFunction

// The line of an outcome that failed. A Write is rolled back first, so a Write fails only when 1C
// rolled it back (concept rule 5). Only script-error carries an error text.
Function FailedLine(Code, ErrorText, Messages, IsWrite, OutcomeLimit)

	If IsWrite Then
		RollBackEveryTransaction();
	EndIf;

	CleanErrorText = Undefined;

	If ErrorText <> Undefined Then
		CleanErrorText = CleanText(ErrorText);
	EndIf;

	Return LineOf(OutcomeOf("failed", Code, "null", 4, CleanErrorText, Messages, OutcomeLimit));

EndFunction

// The line of the outcome Unknown transaction-ended, which carries no result, no error text and
// no messages.
Function UnknownLine(OutcomeLimit)

	Return LineOf(OutcomeOf("unknown", "transaction-ended", "null", 4, Undefined, New Array, OutcomeLimit));

EndFunction

// Rolls back the transaction of the Write, with any that the script began inside it and left
// open.
Procedure RollBackEveryTransaction()

	While TransactionActive() Do
		RollbackTransaction();
	EndDo;

EndProcedure

// The line that Run returns: the size the executor measured, and the outcome object.
Function LineOf(Outcome)

	Return "{""size"":" + XMLString(Outcome.Size) + ",""outcome"":" + Outcome.Text + "}";

EndFunction

// The JSON text of the result, written with the line breaks of the JSON writer off, or Undefined
// when the result cannot be written: it holds a value of a type that concept section 3 does not
// allow, such as a reference, a Map key that is not a string, a string with U+0000 or a lone
// surrogate, or it nests deeper than 64 levels (docs/protocol.md section 8).
Function ResultJsonOf(Result)

	Valid = True;
	Prepared = PreparedValue(Result, 1, Valid);

	If Not Valid Then
		Return Undefined;
	EndIf;

	// The writer refuses nothing that the checks let through; the executor still never raises.
	Try
		Writer = New JSONWriter;
		Writer.SetString(New JSONWriterSettings(JSONLineBreak.None));
		WriteJSON(Writer, Prepared);
		Return Writer.Close();
	Except
		Return Undefined;
	EndTry;

EndFunction

// A copy of the value with every Null replaced by Undefined, since WriteJSON refuses Null. Level
// is the level of the value when it is an array or an object: 1 for the result itself, as section
// 2 of docs/protocol.md counts. Valid becomes False when the value cannot be a result.
Function PreparedValue(Value, Level, Valid)

	ValueType = TypeOf(Value);

	If ValueType = Type("Null") Then
		Return Undefined;
	EndIf;

	If ValueType = Type("String") Then
		If ForbiddenPosition(Value, 1) > 0 Then
			Valid = False;
		EndIf;
		Return Value;
	EndIf;

	If ValueType = Type("Undefined") Or ValueType = Type("Number") Or ValueType = Type("Boolean") Or ValueType = Type("Date") Then
		Return Value;
	EndIf;

	If Level > 64 Then
		Valid = False;
		Return Undefined;
	EndIf;

	If ValueType = Type("Array") Then
		Copy = New Array;
		For Each Item In Value Do
			Copy.Add(PreparedValue(Item, Level + 1, Valid));
			If Not Valid Then
				Return Undefined;
			EndIf;
		EndDo;
		Return Copy;
	EndIf;

	If ValueType = Type("Structure") Then
		Copy = New Structure;
		For Each Item In Value Do
			Copy.Insert(Item.Key, PreparedValue(Item.Value, Level + 1, Valid));
			If Not Valid Then
				Return Undefined;
			EndIf;
		EndDo;
		Return Copy;
	EndIf;

	If ValueType = Type("Map") Then
		Copy = New Map;
		For Each Item In Value Do
			If TypeOf(Item.Key) <> Type("String") Then
				Valid = False;
				Return Undefined;
			EndIf;
			If ForbiddenPosition(Item.Key, 1) > 0 Then
				Valid = False;
				Return Undefined;
			EndIf;
			Copy.Insert(Item.Key, PreparedValue(Item.Value, Level + 1, Valid));
			If Not Valid Then
				Return Undefined;
			EndIf;
		EndDo;
		Return Copy;
	EndIf;

	Valid = False;
	Return Undefined;

EndFunction

// The outcome object of docs/protocol.md section 8, as a structure of its Text and its Size in
// bytes of UTF-8. The result comes as its JSON text and the size of that text; the error text, or
// Undefined, and the messages are clean. When the object does not fit the limit whole, the error
// text and then the messages, in their order, are trimmed to fit it, never inside a surrogate
// pair, and messagesTrimmed is true (concept rule 5). Size is then beyond the limit only when the
// object does not fit even with its texts left out, which within the limits of the protocol only
// a result can cause.
Function OutcomeOf(Kind, Code, ResultJson, ResultSize, Val ErrorText, Messages, OutcomeLimit)

	If Code = Undefined Then
		CodeJson = "null";
	Else
		CodeJson = """" + Code + """";
	EndIf;

	Head = "{""kind"":""" + Kind + """,""code"":" + CodeJson + ",""result"":";
	ErrorTextHead = ",""errorText"":";
	MessagesHead = ",""messages"":[";
	Tail = "],""messagesTrimmed"":false}";

	// Everything but the result, the error text and the messages is ASCII, one byte a character.
	FixedSize = StrLen(Head) + ResultSize + StrLen(ErrorTextHead) + StrLen(MessagesHead);

	If ErrorText = Undefined Then
		TextsSize = 4;
	Else
		TextsSize = JsonStringSize(ErrorText);
	EndIf;

	For Each MessageText In Messages Do
		TextsSize = TextsSize + JsonStringSize(MessageText);
	EndDo;

	// The commas between the messages.
	If Messages.Count() > 1 Then
		TextsSize = TextsSize + Messages.Count() - 1;
	EndIf;

	Size = FixedSize + TextsSize + StrLen(Tail);
	Kept = Messages;

	If Size > OutcomeLimit Then

		// What is left of the limit goes to the error text, then to the messages in their order,
		// and nothing goes after a text that was cut. The room is counted with the tail that ends
		// in false, one byte longer than the one written, so that the texts never fit whole in it.
		Room = OutcomeLimit - FixedSize - StrLen(Tail);
		KeptErrorText = ErrorText;
		KeptSize = 4;
		KeptMessages = New Array;
		ErrorTextCut = False;

		If ErrorText <> Undefined Then
			Length = FittingLength(ErrorText, Room, KeptSize);
			ErrorTextCut = (Length < StrLen(ErrorText));
			KeptErrorText = Left(ErrorText, Length);
		EndIf;

		If Not ErrorTextCut Then
			MessagesSize = 0;
			KeptMessages = FittingMessages(Messages, Room - KeptSize, MessagesSize);
			KeptSize = KeptSize + MessagesSize;
		EndIf;

		// Texts that cannot be shorter stay as they are, and so does the size beyond the limit:
		// messagesTrimmed is true only when something was cut.
		If KeptSize < TextsSize Then
			ErrorText = KeptErrorText;
			Kept = KeptMessages;
			Tail = "],""messagesTrimmed"":true}";
			Size = FixedSize + KeptSize + StrLen(Tail);
		EndIf;

	EndIf;

	If ErrorText = Undefined Then
		ErrorTextJson = "null";
	Else
		ErrorTextJson = JsonString(ErrorText);
	EndIf;

	Items = "";

	For Each MessageText In Kept Do
		If Items <> "" Then
			Items = Items + ",";
		EndIf;
		Items = Items + JsonString(MessageText);
	EndDo;

	Outcome = New Structure("Text, Size");
	Outcome.Text = Head + ResultJson + ErrorTextHead + ErrorTextJson + MessagesHead + Items + Tail;
	Outcome.Size = Size;

	Return Outcome;

EndFunction

// The messages at the start of the list, the last of them perhaps cut, whose JSON strings with
// the commas between them fit the room in bytes of UTF-8. Size becomes their size. A message of
// which no character fits is left out, with every message after it.
Function FittingMessages(Messages, Room, Size)

	Kept = New Array;
	Size = 0;

	For Each MessageText In Messages Do

		Separator = 0;
		If Kept.Count() > 0 Then
			Separator = 1;
		EndIf;

		MessageRoom = Room - Size - Separator;
		MessageSize = 0;
		Length = FittingLength(MessageText, MessageRoom, MessageSize);

		If MessageSize > MessageRoom Or (Length = 0 And StrLen(MessageText) > 0) Then
			Break;
		EndIf;

		Kept.Add(Left(MessageText, Length));
		Size = Size + Separator + MessageSize;

		If Length < StrLen(MessageText) Then
			Break;
		EndIf;

	EndDo;

	Return Kept;

EndFunction

// The text with each U+0000 and each lone surrogate replaced with U+FFFD, so that it is valid
// Unicode and its UTF-8 is the same wherever it is encoded (docs/protocol.md section 8).
Function CleanText(Text)

	Position = ForbiddenPosition(Text, 1);

	If Position = 0 Then
		Return Text;
	EndIf;

	Clean = "";
	Start = 1;

	While Position > 0 Do
		Clean = Clean + Mid(Text, Start, Position - Start) + Char(65533);
		Start = Position + 1;
		Position = ForbiddenPosition(Text, Start);
	EndDo;

	Return Clean + Mid(Text, Start);

EndFunction

// The position of the first U+0000 or lone surrogate in the text at or after From, or 0 when
// there is none. A high surrogate is whole only with a low surrogate right after it.
Function ForbiddenPosition(Text, From)

	Length = StrLen(Text);
	Position = From;

	While Position <= Length Do

		Code = CharCode(Text, Position);

		If Code = 0 Then
			Return Position;
		ElsIf Code >= 55296 And Code <= 56319 Then
			If Position = Length Then
				Return Position;
			EndIf;
			Following = CharCode(Text, Position + 1);
			If Following < 56320 Or Following > 57343 Then
				Return Position;
			EndIf;
			Position = Position + 2;
		ElsIf Code >= 56320 And Code <= 57343 Then
			Return Position;
		Else
			Position = Position + 1;
		EndIf;

	EndDo;

	Return 0;

EndFunction

// The clean text as a JSON string, as section 2 of docs/protocol.md writes one: a quote and a
// backslash escaped with a backslash, the control characters U+0001 to U+001F as \b, \t, \n, \f
// and \r where those exist and as \u00XX otherwise, and every other character as it is.
// FittingLength counts these escapes.
Function JsonString(Text)

	// The backslashes first, so that the escapes written after them are not doubled.
	Escaped = StrReplace(Text, "\", "\\");
	Escaped = StrReplace(Escaped, """", "\""");

	For Code = 1 To 31 Do
		Character = Char(Code);
		If Find(Escaped, Character) > 0 Then
			Escaped = StrReplace(Escaped, Character, ControlEscape(Code));
		EndIf;
	EndDo;

	Return """" + Escaped + """";

EndFunction

Function ControlEscape(Code)

	If Code = 8 Then
		Return "\b";
	ElsIf Code = 9 Then
		Return "\t";
	ElsIf Code = 10 Then
		Return "\n";
	ElsIf Code = 12 Then
		Return "\f";
	ElsIf Code = 13 Then
		Return "\r";
	EndIf;

	Digits = "0123456789ABCDEF";

	Return "\u00" + Mid(Digits, Int(Code / 16) + 1, 1) + Mid(Digits, Code % 16 + 1, 1);

EndFunction

// The size in bytes of the UTF-8 of the clean text as JsonString writes it, quotes included.
Function JsonStringSize(Text)

	Size = 0;
	FittingLength(Text, StrLen(Text) * 6 + 2, Size);

	Return Size;

EndFunction

// The number of characters at the start of the clean text whose JSON string, as JsonString writes
// it, fits the budget in bytes of UTF-8. Size becomes the size of that string, quotes included: 2
// for the empty string, which is the least, even when the budget is smaller. A surrogate pair is
// counted whole, so the number never ends inside one.
Function FittingLength(Text, Budget, Size)

	Size = 2;
	Length = StrLen(Text);
	Position = 1;

	While Position <= Length Do

		Code = CharCode(Text, Position);
		Step = 1;

		If Code >= 55296 And Code <= 56319 Then
			Step = 2;
			CharacterSize = 4;
		ElsIf Code = 34 Or Code = 92 Then
			CharacterSize = 2;
		ElsIf Code = 8 Or Code = 9 Or Code = 10 Or Code = 12 Or Code = 13 Then
			CharacterSize = 2;
		ElsIf Code < 32 Then
			CharacterSize = 6;
		ElsIf Code < 128 Then
			CharacterSize = 1;
		ElsIf Code < 2048 Then
			CharacterSize = 2;
		Else
			CharacterSize = 3;
		EndIf;

		If Size + CharacterSize > Budget Then
			Break;
		EndIf;

		Size = Size + CharacterSize;
		Position = Position + Step;

	EndDo;

	Return Position - 1;

EndFunction

// The size in bytes of the UTF-8 of a text that holds no lone surrogate, such as the JSON of a
// checked result: each unit of a surrogate pair counts two of the four bytes of its character.
Function Utf8Size(Text)

	Size = 0;
	Length = StrLen(Text);

	For Position = 1 To Length Do

		Code = CharCode(Text, Position);

		If Code < 128 Then
			Size = Size + 1;
		ElsIf Code < 2048 Then
			Size = Size + 2;
		ElsIf Code >= 55296 And Code <= 57343 Then
			Size = Size + 2;
		Else
			Size = Size + 3;
		EndIf;

	EndDo;

	Return Size;

EndFunction
