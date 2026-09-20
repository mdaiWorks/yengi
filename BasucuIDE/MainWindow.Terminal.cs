using System;
using System.Windows;
using System.Windows.Input;

namespace mdaiAgent;

public partial class MainWindow
{
    private void TxtTerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (_terminalService == null)
            return;

        if (e.Key == Key.Enter)
        {
            RunTerminalCommand();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            var previous = _terminalService.GetPreviousHistory();
            if (!string.IsNullOrEmpty(previous))
            {
                txtTerminalInput.Text = previous;
                txtTerminalInput.CaretIndex = txtTerminalInput.Text.Length;
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            var next = _terminalService.GetNextHistory();
            if (!string.IsNullOrEmpty(next))
            {
                txtTerminalInput.Text = next;
                txtTerminalInput.CaretIndex = txtTerminalInput.Text.Length;
            }
            e.Handled = true;
        }
    }

    private void BtnRunCommand_Click(object sender, RoutedEventArgs e)
    {
        RunTerminalCommand();
    }

    private void BtnKillProcess_Click(object sender, RoutedEventArgs e)
    {
        if (_terminalService == null)
            return;

        if (!_terminalService.KillCurrentProcess())
        {
            AddTerminalMessage(LocalizationManager.Instance.GetString("SurecDurdurulamadiVeyaCalistirilacakSurecYok"));
        }
    }

    private void BtnClearTerminal_Click(object sender, RoutedEventArgs e)
    {
        _terminalService?.Clear();
    }

    private void RunTerminalCommand()
    {
        var command = txtTerminalInput.Text.Trim();
        if (string.IsNullOrEmpty(command) || _terminalService == null)
            return;

        txtTerminalInput.Clear();
        
        if (_terminalService.IsBusy)
        {
            _terminalService.WriteInput(command);
        }
        else
        {
            RunBackground(_terminalService.ExecuteCommandAsync(command), "TerminalCmd");
        }
    }
}
