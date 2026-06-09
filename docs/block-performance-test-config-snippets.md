# Block Performance Test Config Snippets

This file is meant to be copied into the config debugger as real LoliCode.

- Turn on `Block Profiling` in the debugger before running these snippets.
- Use small themed configs instead of one giant config.
- Duplicate a block several times when you want steadier timing comparisons.

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

## Parse block template

Use this for local parse timing without external HTTP.

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

## Http Request block template

Use a fast stable endpoint for measurement. Keep this separate from pure local blocks.

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

This is a ready-to-run starter config built from the snippets above.

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

## Next expansion pattern

When you add more blocks, keep the same pattern:

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
```
