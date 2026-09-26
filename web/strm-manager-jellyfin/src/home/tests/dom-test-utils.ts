import type { NativeRowComponents } from "../native-row-components";

export class ElementMock {
    className = '';
    textContent: string | null = null;
    readonly children: ElementMock[] = [];
    readonly attributes = new Map<string, string>();
    readonly style = {
        backgroundImage: '',
    };

    private readonly listeners = new Map<
        string,
        Array<() => void>
    >();

    setAttribute(
        name: string,
        value: string,
    ): void {
        this.attributes.set(name, value);
    }

    addEventListener(
        type: string,
        listener: () => void,
    ): void {
        const listeners =
            this.listeners.get(type) ?? [];

        listeners.push(listener);
        this.listeners.set(type, listeners);
    }

    click(): void {
        for (
            const listener of
            this.listeners.get('click') ?? []
        ) {
            listener();
        }
    }

    append(...children: ElementMock[]): void {
        this.children.push(...children);
    }

    appendChild(child: ElementMock): ElementMock {
        this.children.push(child);
        return child;
    }

    replaceChildren(...children: ElementMock[]): void {
        this.children.length = 0;
        this.children.push(...children);
    }
}

export function createNativeRowComponentsMock():
    NativeRowComponents {
    return {
        createScroller: () =>
            new ElementMock() as unknown as HTMLElement,
    };
}

export function createDocumentMock():
    Pick<Document, 'createElement'> {
    return {
        createElement: () =>
            new ElementMock() as unknown as HTMLElement,
    } as Pick<Document, 'createElement'>;
}

export function getChild(
    element: ElementMock,
    index: number,
): ElementMock {
    const child = element.children[index];

    if (!child) {
        throw new Error(
            `Expected child at index ${index}.`,
        );
    }

    return child;
}
