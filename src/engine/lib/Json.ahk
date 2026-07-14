#Requires AutoHotkey v2.0

class Json {
    static Parse(text) {
        parser := JsonParser(text)
        return parser.Parse()
    }
}

class JsonNull {
}

class JsonParser {
    __New(text) {
        this.text := text
        this.position := 1
        this.length := StrLen(text)
    }

    Parse() {
        value := this.ParseValue()
        this.SkipWhitespace()
        if this.position <= this.length {
            throw Error("JSON contains unexpected trailing content at position " this.position ".")
        }
        return value
    }

    ParseValue() {
        this.SkipWhitespace()
        if this.position > this.length {
            throw Error("JSON ended before a value was complete.")
        }

        character := SubStr(this.text, this.position, 1)
        switch character {
            case "{":
                return this.ParseObject()
            case "[":
                return this.ParseArray()
            case '"':
                return this.ParseString()
            case "t":
                return this.ParseLiteral("true", true)
            case "f":
                return this.ParseLiteral("false", false)
            case "n":
                return this.ParseLiteral("null", JsonNull())
            default:
                if character = "-" || RegExMatch(character, "\d") {
                    return this.ParseNumber()
                }
                throw Error("JSON contains an unexpected character at position " this.position ".")
        }
    }

    ParseObject() {
        result := Map()
        this.position += 1
        this.SkipWhitespace()
        if this.TakeIf("}") {
            return result
        }

        loop {
            this.SkipWhitespace()
            if SubStr(this.text, this.position, 1) != '"' {
                throw Error("JSON object keys must be strings at position " this.position ".")
            }
            key := this.ParseString()
            this.SkipWhitespace()
            this.Expect(":")
            result[key] := this.ParseValue()
            this.SkipWhitespace()
            if this.TakeIf("}") {
                break
            }
            this.Expect(",")
        }
        return result
    }

    ParseArray() {
        result := []
        this.position += 1
        this.SkipWhitespace()
        if this.TakeIf("]") {
            return result
        }

        loop {
            result.Push(this.ParseValue())
            this.SkipWhitespace()
            if this.TakeIf("]") {
                break
            }
            this.Expect(",")
        }
        return result
    }

    ParseString() {
        this.Expect('"')
        result := ""
        loop {
            if this.position > this.length {
                throw Error("JSON string was not terminated.")
            }
            character := SubStr(this.text, this.position, 1)
            this.position += 1
            if character = '"' {
                return result
            }
            if character != "\" {
                result .= character
                continue
            }

            if this.position > this.length {
                throw Error("JSON escape sequence was not complete.")
            }
            escaped := SubStr(this.text, this.position, 1)
            this.position += 1
            switch escaped {
                case '"':
                    result .= '"'
                case "\":
                    result .= "\"
                case "/":
                    result .= "/"
                case "b":
                    result .= Chr(8)
                case "f":
                    result .= Chr(12)
                case "n":
                    result .= "`n"
                case "r":
                    result .= "`r"
                case "t":
                    result .= "`t"
                case "u":
                    result .= this.ParseUnicodeEscape()
                default:
                    throw Error("JSON contains an unsupported escape sequence at position " (this.position - 1) ".")
            }
        }
    }

    ParseUnicodeEscape() {
        hexadecimal := SubStr(this.text, this.position, 4)
        if StrLen(hexadecimal) != 4 || !RegExMatch(hexadecimal, "^[0-9A-Fa-f]{4}$") {
            throw Error("JSON contains an invalid Unicode escape at position " this.position ".")
        }
        this.position += 4
        codePoint := Integer("0x" hexadecimal)

        if codePoint >= 0xD800 && codePoint <= 0xDBFF
            && SubStr(this.text, this.position, 2) = "\u" {
            lowHexadecimal := SubStr(this.text, this.position + 2, 4)
            if RegExMatch(lowHexadecimal, "^[0-9A-Fa-f]{4}$") {
                lowCodePoint := Integer("0x" lowHexadecimal)
                if lowCodePoint >= 0xDC00 && lowCodePoint <= 0xDFFF {
                    this.position += 6
                    codePoint := 0x10000 + ((codePoint - 0xD800) << 10) + (lowCodePoint - 0xDC00)
                }
            }
        }
        return Chr(codePoint)
    }

    ParseNumber() {
        remainder := SubStr(this.text, this.position)
        if !RegExMatch(remainder, "^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?", &match) {
            throw Error("JSON contains an invalid number at position " this.position ".")
        }
        rawNumber := match[0]
        this.position += StrLen(rawNumber)
        return rawNumber + 0
    }

    ParseLiteral(literal, value) {
        if SubStr(this.text, this.position, StrLen(literal)) != literal {
            throw Error("JSON contains an invalid value at position " this.position ".")
        }
        this.position += StrLen(literal)
        return value
    }

    SkipWhitespace() {
        while this.position <= this.length
            && InStr(" `t`r`n", SubStr(this.text, this.position, 1)) {
            this.position += 1
        }
    }

    Expect(character) {
        if SubStr(this.text, this.position, 1) != character {
            throw Error("Expected '" character "' at position " this.position ".")
        }
        this.position += 1
    }

    TakeIf(character) {
        if SubStr(this.text, this.position, 1) != character {
            return false
        }
        this.position += 1
        return true
    }
}
