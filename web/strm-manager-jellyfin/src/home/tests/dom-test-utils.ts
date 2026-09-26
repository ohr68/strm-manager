export class ElementMock {
    className = '';
    textContent: string | null = null;
    readonly children: ElementMock[] = [];
    readonly attributes = new Map<string, string>();
    readonly style = {
        backgroundImage: '',
    };

    setAttribute(
        name: string,
        value: string,
    ): void {
        this.attributes.set(name, value);
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
