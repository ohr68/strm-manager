const FEEDBACK_VISIBLE_MS = 5_000;

export interface HomeFeedback {
    show(message: string): void;
}

export function createHomeFeedback():
    HomeFeedback {
    let timeoutId:
        number | undefined;

    let element:
        HTMLElement | null = null;

    function remove(): void {
        if (timeoutId !== undefined) {
            window.clearTimeout(
                timeoutId,
            );

            timeoutId = undefined;
        }

        element?.remove();
        element = null;
    }

    return {
        show(message): void {
            remove();

            const feedback =
                document.createElement('div');

            feedback.className =
                'strm-manager-feedback';

            feedback.setAttribute(
                'role',
                'status',
            );

            feedback.setAttribute(
                'aria-live',
                'polite',
            );

            const text =
                document.createElement('span');

            text.className =
                'strm-manager-feedback-message';

            text.textContent = message;

            const close =
                document.createElement('button');

            close.type = 'button';
            close.className =
                'strm-manager-feedback-close';

            close.setAttribute(
                'aria-label',
                'Fechar mensagem',
            );

            close.textContent = '×';

            close.addEventListener(
                'click',
                remove,
            );

            feedback.append(
                text,
                close,
            );

            document.body.appendChild(
                feedback,
            );

            element = feedback;

            timeoutId =
                window.setTimeout(
                    remove,
                    FEEDBACK_VISIBLE_MS,
                );
        },
    };
}
