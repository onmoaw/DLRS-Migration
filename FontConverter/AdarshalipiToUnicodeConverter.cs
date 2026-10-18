using System;
using System.Collections.Generic;
using System.Text;

namespace FontConverter
{
    public static class Converter
    {
        private static readonly Dictionary<char, string> conversionMap = BuildConversionMap();

        public static string ConvertToAdarshalipi(string sample)
        {
            string input = sample;
            StringBuilder sb = new StringBuilder(input.Length);

            // STEP 1: Replace ­ with ®
            foreach (char c in input)
            {
                sb.Append(c == '­' ? '®' : c);
            }
            input = sb.ToString();
            sb.Clear();

            // STEP 2: Kar correction - Move vowel signs before consonants
            for (int i = 0; i < input.Length; i++)
            {
                char current = input[i];
                if ((current == '¢' || current == '­' || current == '®' || current == '¯' || current == '°')
                    && i + 1 < input.Length)
                {
                    sb.Append(input[i + 1]);
                    sb.Append(current);
                    i++;
                }
                else
                {
                    sb.Append(current);
                }
            }
            input = sb.ToString();
            sb.Clear();

            // STEP 3: Ref correction - Move Ñ (ref) before consonants
            for (int i = 0; i < input.Length; i++)
            {
                if (i + 1 < input.Length && input[i + 1] == 'Ñ')
                {
                    sb.Append(input[i + 1]);
                    sb.Append(input[i]);
                    i++;
                }
                else
                {
                    sb.Append(input[i]);
                }
            }
            input = sb.ToString();
            sb.Clear();

            // STEP 4: Combined kar and ref correction
            for (int i = 0; i < input.Length; i++)
            {
                char current = input[i];

                if (i + 2 < input.Length &&
                    (input[i + 2] == '¢' || input[i + 2] == '­' || input[i + 2] == '®') &&
                    input[i + 1] == 'Ñ')
                {
                    sb.Append(input[i + 1]);
                    sb.Append(current);
                    i++;
                }
                else if (i + 1 < input.Length &&
                         (current == '­' || current == '®') &&
                         input[i + 1] == 'É')
                {
                    sb.Append(input[i + 1]);
                    sb.Append(current);
                    i++;
                }
                else if (i + 1 < input.Length &&
                         (current == '¢' || current == '­' || current == '®') &&
                         input[i + 1] == 'Ô')
                {
                    sb.Append(input[i + 1]);
                    sb.Append(current);
                    i++;
                }
                else if (i + 1 < input.Length && current == 'y' && input[i + 1] == '¡')
                {
                    sb.Append(input[i + 1]);
                    sb.Append(current);
                    i++;
                }
                else
                {
                    sb.Append(current);
                }
            }
            input = sb.ToString();
            sb.Clear();

            // STEP 5: Main conversion - Use dictionary for O(1) lookup
            foreach (char c in input)
            {
                if (conversionMap.TryGetValue(c, out string mapped))
                {
                    sb.Append(mapped);
                }
            }

            string output = sb.ToString();
            sb.Clear();

            // STEP 6: First pass - Reorder হসন্ত (্) with vowel marks
            for (int i = 0; i < output.Length; i++)
            {
                if (output[i] == '্' && i + 1 < output.Length &&
                    (output[i + 1] == 'ে' || output[i + 1] == 'ি') &&
                    i + 2 < output.Length)
                {
                    sb.Append(output[i]);
                    sb.Append(output[i + 2]);
                    sb.Append(output[i + 1]);
                    i += 2;
                }
                else
                {
                    sb.Append(output[i]);
                }
            }
            output = sb.ToString();
            sb.Clear();

            // STEP 7: Second pass - Another reordering (same logic)
            for (int i = 0; i < output.Length; i++)
            {
                if (output[i] == '্' && i + 1 < output.Length &&
                    (output[i + 1] == 'ে' || output[i + 1] == 'ি') &&
                    i + 2 < output.Length)
                {
                    sb.Append(output[i]);
                    sb.Append(output[i + 2]);
                    sb.Append(output[i + 1]);
                    i += 2;
                }
                else
                {
                    sb.Append(output[i]);
                }
            }
            output = sb.ToString();
            sb.Clear();

            // STEP 8: Remove duplicate হসন্ত (্্)
            for (int i = 0; i < output.Length; i++)
            {
                if (i + 1 < output.Length && output[i] == '্' && output[i + 1] == '্')
                {
                    i++;
                }
                sb.Append(output[i]);
            }

            // STEP 9: Final replacements for specific character combinations
            string result = sb.ToString();
            result = result.Replace("ত্র্ক", "ক্র")
                           .Replace("ত্রে্ক", "ক্রে")
                           .Replace("ত্রে্‌কা", "ক্রো")
                           .Replace("ত্রি্ক", "ক্রি")
                           .Replace("ত্ত্ক", "ক্ত")
                           .Replace("ত্তে্ক", "ক্তে")
                           .Replace("ত্তি্ক", "ক্তি")
                           .Replace("পি্র", "প্রি")
                           .Replace("পে্র", "প্রে")
                           .Replace("ভর্ূ", "র্ভূ")
                           .Replace("e0", "_")
                           .Replace("শর্ব", "র্শ্ব")
                           .Replace("শের্ব", "র্শ্বে")
                           .Replace("তর্ু", "র্তু")
                           .Replace("ষ্ঞ", "ষ্ণ")
                           .Replace("ক্‌ল", "ক্ল")
                           .Replace("কি্‌ল", "ক্লি")
                           .Replace("কি্লনি", "ক্লিনি")
                           //.Replace("বর্", "র্ব") (need deeper case study)
                           //.Replace("গর্", "র্গ") (need deeper case study)
                           .Replace("ম্র্ম", "র্ম্ম")
                           .Replace("স্টি্র", "স্ট্রি")
                           .Replace("শি্ব", "শ্বি")
                           .Replace("শে্ব", "শ্বে")
                           .Replace("শি্র", "শ্রি")
                           .Replace("সে্ন", "স্নে")
                           .Replace("ত্র্তু", "র্ত্তু")
                           .Replace("ঙ্গঁু", "ঙ্গুঁ")
                           .Replace("টে্র", "ট্রে")
                           .Replace("ষ্ঞ", "ষ্ণ")
                           .Replace("ম্রি্ম", "র্ম্মি")
                           .Replace("বে্র", "ব্রে")
                           .Replace("চ্র্চ", "র্চ্চ")
                           .Replace("গি্র", "গ্রি")
                           .Replace("গে্র", "গ্রে")
                           .Replace("পর্ু", "র্পু")
                           .Replace("স্র্বন", "স্বর্ন")
                           .Replace("অা", "আ");


            return result;
        }

        private static Dictionary<char, string> BuildConversionMap()
        {
            char[] compareData = new char[]
            {
                '\n', ' ', '\t', '!', '"', '#', '$', '%', '&', '\'', '(', ')', '*', '+', ',', '-', '.', '/', '0','1','2','3','4','5','6','7','8','9', ':',';','<','=','>','?','@',
                'A','B','C','D','E','F','G','H','I','J','K','L','M', 'N','O','P','Q','R','S','T','U','V','W','X','Y','Z', '[','\\',']','^','_','`', 'a','b','c','d','e','f','g','h','i','j','k','l','m',
                'n','o','p','q','r','s','t','u','v','w','x','y','z', '{','|','}','~', '‚','ƒ','„','…','†','‡','ˆ','‰','Š','‹','Œ', '‘','’','“','”','•','–','—','˜','™','š','›','œ','Ÿ',
                '¡','¢','£','¤','¥','¦','§','¨','©','ª','«','¬','­', '®','¯','°','±','²','³','´','µ','¶','·','¸','¹','º', '»','¼','½','¾','¿', 'À','Á','Â','Ã','Ä','Å','Æ','Ç','È','É','Ê','Ë',
                'Ì','Ð','Ñ','Ò','Ó','Ô','Õ','Ö','×','Ø','Ù','Ú','Û', 'Ü','Ý','Þ','ß', 'à','á','â','ã','ä','å','æ','ç','è','é','ê','ë', 'ì','í','î','ï','ð','ñ','ò','ó','ô','õ','ö','÷', 'ø','ù','ú','û','ü','ý','þ'
            };

            string[] unicodeData = {
                "\n", " ", "\t", "!", "'", "#", "$", "%", "&", "'", "(", ")", "*", "+", ",", "-", ".", "/", "০", "১", "২", "৩", "৪", "৫", "৬", "৭", "৮", "৯", ":", ";", "<", "=", ">", "?", "@", "অ", "আ", "ই", 
                "ঈ", "উ", "ঊ", "ঋ", "এ", "ঐ", "ও", "ঔ", "ক", "খ", "গ", "ঘ", "ঙ", "চ", "ছ", "জ", "ঝ", "ঞ", "ট", "ঠ", "ড", "ঢ", "ণ", "[", "\\", "]", "×", "e0", "÷", "ত", "থ", "দ", "ধ", "ন", "প", "ফ", "ব", 
                "ভ", "ম", "য", "র", "ল", "শ", "ষ", "স", "হ", "ক্ষ", "ড়", "ঢ়", "য়", "ৎ", "ং", "ঃ", "ঁ", "।", "{", "|", "}", "্র", "ক্ক", "ক্ট", "ক্স", "গু", "গ্‌গ", "গ্ধ", "ঙ্ক", "ঙ্গ", "চ্‌ঞ", "জ্জ", "জ্ঝ", "জ্ঞ", "ঞ্চ", "ঞ্ছ",
                "ঞ্জ", "ঞ্ঝ", "ট্ট", "ড্ড", "ণ্ঠ", "ণ্ড", "ত্ত", "ত্থ", "ত্র", "দ্দ", "া", "ি", "ী", "ু", "ু", "ু", "ূ", "ূ", "ূ", "ৃ", "ৃ", "×", "ে", "ে", "ৈ", "ৈ", "ৗ", "্ক", "গ্", "ঙ্", "চ্", "জ", "্ঞ", "ণ্", "্ত", "ত্ত", "্ত্র",
                "্দ", "্ধ", "ন্", "ন্", "্ন", "্ন", "×", "প্", "্ব", "্ব", "্ব", "ম্", "্ম", "্য", "্র", "্র", "্র", "্র", "র্", "ল্", "্ল", "্ল", "শ্", "ষ্", "ষ্", "স্", "স্", "্‌", "্থ", "দ্ধ", "×", "দ্ধ্ব", "×", 
                "দ্ব", "দ্ভ", "দ্র", "ন্ঠ", "ন্ড", "ন্ধ", "ন্ন", "প্ত", "প্প", "ফ্র", "ব্জ", "ব্দ", "ব্ধ", "×", "ব্ব", "ভ্র", "ম্ব", "ম্ভ", "ম্ভ্র", "ল্ক", "ল্ড", "ল্ল", "শু", "শ্ত", "ষ্ট", "ষ্ঠ", "স্ক", "স্ক্র", "স্ব", "হু", "ক্ষ্ম"
            };

            var map = new Dictionary<char, string>(compareData.Length);
            for (int i = 0; i < compareData.Length; i++)
            {
                map[compareData[i]] = unicodeData[i];
            }
            return map;
        }
    }
}
