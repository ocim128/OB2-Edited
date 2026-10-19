# LoliCode Block Snippets

Hand-written LoliCode examples for exercising blocks in the config debugger. Useful when you need a known-good config without building one in the stacker, or when checking that a parser/transpiler change still accepts real syntax.

How to use:

- Paste a snippet into the LoliCode editor of a new config (`ConfigMode.LoliCode`).
- Run it in the debugger; the log shows each block's output.
- Duplicate a block when you want to compare timings between two variants.

Syntax notes, verified against `RuriLib/Models/Blocks/AutoBlockInstance.cs` and the custom block partials:

- `BLOCK:<Id>` — the id is the method name of the `[Block]` method (`ConstantString`, `CharAt`, `HttpRequest`, …), not the display name.
- `LABEL:` is optional and only labels the block in logs.
- `SAFE` makes the block catch exceptions and store them in `data.ERROR` instead of aborting the config.
- `=> VAR @name` assigns the return value to a variable.
- `ENDBLOCK` terminates every block.

## Local string and constant blocks

```text
BLOCK:ConstantString
LABEL:otp
  value = "111"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:ConstantString
LABEL:otp
  value = "111"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:ConstantString
LABEL:otp
  value = "111"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:ConstantString
LABEL:otp
  value = "111"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:ConstantString
LABEL:otp
  value = "222"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:ConstantDictionary
  value = {("a", "a"), ("b", "b")}
  SAFE
  => VAR @constantDictionaryOutput
ENDBLOCK

BLOCK:ConstantList
  value = ["1", "2"]
  SAFE
  => VAR @constantListOutput
ENDBLOCK

BLOCK:ConstantString
  value = "ggg"
  => VAR @constantStringOutput
ENDBLOCK

BLOCK:ConstantString
  value = "aa"
  => VAR @constantStringOutput
ENDBLOCK
```

## Local string function blocks

```text
BLOCK:CharAt
  input = @otp
  SAFE
  => VAR @charAtOutput
ENDBLOCK

BLOCK:EncodeHTMLEntities
  input = @otp
  SAFE
  => VAR @encodeHTMLEntitiesOutput
ENDBLOCK

BLOCK:Length
  input = @otp
  SAFE
  => VAR @lengthOutput
ENDBLOCK

BLOCK:RandomString
  input = "?d"
  SAFE
  => VAR @randomStringOutput
ENDBLOCK

BLOCK:RegexReplace
  original = @otp
  SAFE
  => VAR @regexReplaceOutput
ENDBLOCK

BLOCK:Replace
  original = @otp
  SAFE
  => VAR @replaceOutput
ENDBLOCK

BLOCK:Reverse
  input = @otp
  SAFE
  => VAR @reverseOutput
ENDBLOCK

BLOCK:Substring
  input = @otp
  SAFE
  => VAR @substringOutput
ENDBLOCK

BLOCK:ToLowercase
  input = @otp
  SAFE
  => VAR @toLowercaseOutput
ENDBLOCK

BLOCK:ToUppercase
  input = @otp
  SAFE
  => VAR @toUppercaseOutput
ENDBLOCK

BLOCK:Unescape
  input = @otp
  SAFE
  => VAR @unescapeOutput
ENDBLOCK

BLOCK:DecodeHTMLEntities
  input = @otp
  SAFE
  => VAR @decodeHTMLEntitiesOutput
ENDBLOCK
```

## Delay blocks

```text
BLOCK:Delay
  milliseconds = 222
ENDBLOCK

BLOCK:Delay
  milliseconds = 222
ENDBLOCK
```

## Script block

```text
BLOCK:Script
INTERPRETER:Jint
INPUT 
BEGIN SCRIPT
var a = "111";return a
END SCRIPT
OUTPUT String @a
ENDBLOCK
```

## Keycheck block

```text
BLOCK:Keycheck
  banIfNoMatch = False
  KEYCHAIN FAIL OR
    STRINGKEY @randomStringOutput Contains "3"
  KEYCHAIN SUCCESS OR
    STRINGKEY @randomStringOutput Contains "2"
  KEYCHAIN CUSTOM OR
    STRINGKEY @randomStringOutput Contains "4"
  KEYCHAIN NONE OR
    STRINGKEY @randomStringOutput Contains "5"
  KEYCHAIN RETRY OR
    STRINGKEY @randomStringOutput Contains "6"
  KEYCHAIN BAN OR
    STRINGKEY @randomStringOutput Contains "7"
  KEYCHAIN ERROR OR
    STRINGKEY @randomStringOutput Contains "8"
ENDBLOCK
```

## Parse block

Uses a constant as input so the block can be exercised without network access.

```text
BLOCK:ConstantString
LABEL:source
  value = "hello <b>world</b> 123"
  => VAR @source
ENDBLOCK

BLOCK:Parse
  input = @source
  leftDelim = "hello "
  rightDelim = " 123"
  => VAR @parseOutput
ENDBLOCK
```

## Http Request block

Minimal request block. Keep network blocks in their own config so a failing endpoint does not hide local block behavior.

```text
BLOCK:HttpRequest
  url = "https://example.com"
  method = GET
  autoRedirect = True
  maxNumberOfRedirects = 8
  readResponseContent = True
  urlEncodeContent = False
  absoluteUriInFirstLine = False
  httpLibrary = RuriLibHttp
  useTlsFingerprinting = False
  timeoutMilliseconds = 15000
  httpVersion = "1.1"
ENDBLOCK
```

## Combined sample config

A runnable config built from the snippets above. The five `ConstantString` blocks all use `LABEL:otp`; the label is cosmetic, and each block reassigns `@otp`.

```text
BLOCK:ConstantString
LABEL:otp
  value = "111"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:ConstantString
LABEL:otp
  value = "111"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:ConstantString
LABEL:otp
  value = "111"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:ConstantString
LABEL:otp
  value = "111"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:ConstantString
LABEL:otp
  value = "222"
  SAFE
  => VAR @otp
ENDBLOCK

BLOCK:Script
INTERPRETER:Jint
INPUT 
BEGIN SCRIPT
var a = "111";return a
END SCRIPT
OUTPUT String @a
ENDBLOCK

BLOCK:CharAt
  input = @otp
  SAFE
  => VAR @charAtOutput
ENDBLOCK

BLOCK:EncodeHTMLEntities
  input = @otp
  SAFE
  => VAR @encodeHTMLEntitiesOutput
ENDBLOCK

BLOCK:ConstantDictionary
  value = {("a", "a"), ("b", "b")}
  SAFE
  => VAR @constantDictionaryOutput
ENDBLOCK

BLOCK:Length
  input = @otp
  SAFE
  => VAR @lengthOutput
ENDBLOCK

BLOCK:RandomString
  input = "?d"
  SAFE
  => VAR @randomStringOutput
ENDBLOCK

BLOCK:RegexReplace
  original = @otp
  SAFE
  => VAR @regexReplaceOutput
ENDBLOCK

BLOCK:Replace
  original = @otp
  SAFE
  => VAR @replaceOutput
ENDBLOCK

BLOCK:Reverse
  input = @otp
  SAFE
  => VAR @reverseOutput
ENDBLOCK

BLOCK:Substring
  input = @otp
  SAFE
  => VAR @substringOutput
ENDBLOCK

BLOCK:ToLowercase
  input = @otp
  SAFE
  => VAR @toLowercaseOutput
ENDBLOCK

BLOCK:ToUppercase
  input = @otp
  SAFE
  => VAR @toUppercaseOutput
ENDBLOCK

BLOCK:Unescape
  input = @otp
  SAFE
  => VAR @unescapeOutput
ENDBLOCK

BLOCK:ConstantList
  value = ["1", "2"]
  SAFE
  => VAR @constantListOutput
ENDBLOCK

BLOCK:DecodeHTMLEntities
  input = @otp
  SAFE
  => VAR @decodeHTMLEntitiesOutput
ENDBLOCK

BLOCK:Delay
  milliseconds = 222
ENDBLOCK

BLOCK:Delay
  milliseconds = 222
ENDBLOCK

BLOCK:ConstantString
  value = "ggg"
  => VAR @constantStringOutput
ENDBLOCK

BLOCK:ConstantString
  value = "aa"
  => VAR @constantStringOutput
ENDBLOCK

BLOCK:Keycheck
  banIfNoMatch = False
  KEYCHAIN FAIL OR
    STRINGKEY @randomStringOutput Contains "3"
  KEYCHAIN SUCCESS OR
    STRINGKEY @randomStringOutput Contains "2"
  KEYCHAIN CUSTOM OR
    STRINGKEY @randomStringOutput Contains "4"
  KEYCHAIN NONE OR
    STRINGKEY @randomStringOutput Contains "5"
  KEYCHAIN RETRY OR
    STRINGKEY @randomStringOutput Contains "6"
  KEYCHAIN BAN OR
    STRINGKEY @randomStringOutput Contains "7"
  KEYCHAIN ERROR OR
    STRINGKEY @randomStringOutput Contains "8"
ENDBLOCK
```

## Adding More Blocks

Keep the same shape:

```text
BLOCK:BlockIdHere
  requiredParam = "safe-local-value"
  optionalParam = "fixed-value"
  SAFE
  => VAR @outputVar
ENDBLOCK
```

Use separate configs for:

- pure local blocks
- HTTP and network blocks
- browser blocks
- file system blocks
- captcha/provider blocks
- mail blocks
- Android blocks
