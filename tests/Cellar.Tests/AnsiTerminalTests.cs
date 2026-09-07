using System;
using System.Text;
using Cellar.Terminal;

namespace Cellar.Tests;

public class AnsiTerminalTests
{
    [Theory]
    [InlineData(20127, 20127)]
    [InlineData(1200, 1200)]
    [InlineData(65001, 65001)]
    [InlineData(20127, 1200)]
    public void Construction_UsesUtf8AndDisposalRestoresEncodings(int inputCodePage, int outputCodePage)
    {
        Encoding input = Console.InputEncoding;
        Encoding output = Console.OutputEncoding;
        try
        {
            Console.InputEncoding = Encoding.GetEncoding(inputCodePage);
            Console.OutputEncoding = Encoding.GetEncoding(outputCodePage);
            using (var terminal = new AnsiTerminal())
            {
                Assert.Equal(65001, Console.InputEncoding.CodePage);
                Assert.Equal(65001, Console.OutputEncoding.CodePage);
                Assert.Empty(Console.OutputEncoding.GetPreamble());
            }
            Assert.Equal(inputCodePage, Console.InputEncoding.CodePage);
            Assert.Equal(outputCodePage, Console.OutputEncoding.CodePage);
        }
        finally
        {
            Console.InputEncoding = input;
            Console.OutputEncoding = output;
        }
    }

    [Fact]
    public void Disposal_AfterApplicationFailureRestoresEncodings()
    {
        Encoding input = Console.InputEncoding;
        Encoding output = Console.OutputEncoding;
        try
        {
            Console.InputEncoding = Encoding.ASCII;
            Console.OutputEncoding = Encoding.Unicode;
            static void RunApplication()
            {
                using var terminal = new AnsiTerminal();
                throw new InvalidOperationException("Application failed.");
            }
            Assert.Throws<InvalidOperationException>(RunApplication);
            Assert.Equal(Encoding.ASCII.CodePage, Console.InputEncoding.CodePage);
            Assert.Equal(Encoding.Unicode.CodePage, Console.OutputEncoding.CodePage);
        }
        finally
        {
            Console.InputEncoding = input;
            Console.OutputEncoding = output;
        }
    }

    [Fact]
    public void RepeatedDisposal_DoesNotOverwriteLaterConsoleSettings()
    {
        Encoding input = Console.InputEncoding;
        Encoding output = Console.OutputEncoding;
        try
        {
            using var terminal = new AnsiTerminal();
            terminal.Dispose();
            Console.InputEncoding = Encoding.ASCII;
            Console.OutputEncoding = Encoding.Unicode;
            terminal.Dispose();
            Assert.Equal(Encoding.ASCII.CodePage, Console.InputEncoding.CodePage);
            Assert.Equal(Encoding.Unicode.CodePage, Console.OutputEncoding.CodePage);
        }
        finally
        {
            Console.InputEncoding = input;
            Console.OutputEncoding = output;
        }
    }
}
