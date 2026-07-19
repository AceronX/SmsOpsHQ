using System.Windows;
using SmsOpsHQ.Desktop.ViewModels;

namespace SmsOpsHQ.Desktop.Services;

public interface ILateTicketPullConfirmationService
{
    bool ConfirmMove(LateCustomerItem ticket);
}

public sealed class LateTicketPullConfirmationService : ILateTicketPullConfirmationService
{
    public bool ConfirmMove(LateCustomerItem ticket)
    {
        string customer = string.IsNullOrWhiteSpace(ticket.FullName)
            ? $"customer {ticket.CustomerKey}"
            : ticket.FullName;
        return MessageBox.Show(
                $"Move ticket #{ticket.TicketNo} for {customer} to the Pull List?",
                "Move to Pull List",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            == MessageBoxResult.Yes;
    }
}
