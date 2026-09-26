import type {
    NativeRowComponents,
} from '../native-row-components';

export class ElementMock {
    className = '';
    textContent: string | null = null;
    disabled = false;
    hidden = false;

    readonly children: ElementMock[] = [];
    readonly attributes = new Map<string, string>();

    readonly style = {
        backgroundImage: '',
    };

    readonly classList = {
        add: (...tokens: string[]): void => {
            const classes =
                this.getClassNames();

            for (const token of tokens) {
                classes.add(token);
            }

            this.className =
                Array.from(classes).join(' ');
        },

        remove: (...tokens: string[]): void => {
            const classes =
                this.getClassNames();

            for (const token of tokens) {
                classes.delete(token);
            }

            this.className =
                Array.from(classes).join(' ');
        },

        contains: (token: string): boolean => {
            return this
                .getClassNames()
                .has(token);
        },
    };

    private readonly listeners = new Map<
        string,
        Array<() => void>
    >();

    private getClassNames(): Set<string> {
        return new Set(
            this.className
                .split(/\s+/)
                .filter(Boolean),
        );
    }

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
        this.listeners.set(
            type,
            listeners,
        );
    }

    click(): void {
        for (
            const listener of
            this.listeners.get('click') ?? []
        ) {
            listener();
        }
    }

    append(
        ...children: ElementMock[]
    ): void {
        this.children.push(...children);
    }

    appendChild(
        child: ElementMock,
    ): ElementMock {
        this.children.push(child);

        return child;
    }

    replaceChildren(
        ...children: ElementMock[]
    ): void {
        this.children.length = 0;
        this.children.push(...children);
    }
}

export function createNativeRowComponentsMock():
    NativeRowComponents {
    return {
        createScroller: () =>
            new ElementMock() as unknown as HTMLElement,

        createItemsContainer: () =>
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
    const child =
        element.children[index];

    if (!child) {
        throw new Error(
            `Expected child at index ${index}.`,
        );
    }

    return child;
}